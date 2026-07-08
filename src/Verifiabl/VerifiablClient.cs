using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Verifiabl.Internal;

namespace Verifiabl;

/// <summary>
/// Typed client for the Verifiabl issuer API.
/// </summary>
/// <remarks>
/// <para>
/// With OAuth client credentials the client fetches, caches, and refreshes
/// access tokens automatically; a request that fails with 401 is retried exactly
/// once with a fresh token. The client is thread-safe and intended to be created
/// once and reused.
/// </para>
/// <para>
/// API methods are virtual so the client can be mocked in tests.
/// </para>
/// </remarks>
public class VerifiablClient
{
    /// <summary>Maximum records per batch request. Matches the API's limit.</summary>
    public const int MaxBatchRecords = Wire.MaxBatchRecords;

    private const string IssuerScope = "verifiabl:issuer";

    private static readonly string[] VerifiablAuthHosts =
    [
        "auth.verifiabl.io",
        "auth.sandbox.verifiabl.io",
    ];

    /// <summary>Maximum time before expiry that an OAuth token is treated as stale.</summary>
    private static readonly TimeSpan MaxTokenRefreshBuffer = TimeSpan.FromSeconds(60);

    private static readonly Lazy<HttpClient> SharedHttpClient = new(CreateSharedHttpClient);

    private readonly VerifiablAuth _auth;
    private readonly string _tokenUrl;
    private readonly string _issuerBaseUrl;
    private readonly TimeSpan _timeout;
    private readonly HttpClient _httpClient;
    private readonly Action<VerifiablRequestEvent>? _onRequest;
    private readonly Action<VerifiablResponseEvent>? _onResponse;
    private readonly Action<VerifiablErrorEvent>? _onError;
    private readonly int _maxRetries;

    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private volatile CachedToken? _tokenCache;

    /// <summary>
    /// Sent as the User-Agent on every request so Verifiabl support can identify
    /// the SDK version behind a call. Unlike some SDKs this carries no usage
    /// telemetry: PII never leaves the provider, and neither does anything else.
    /// </summary>
    private static readonly string UserAgent = BuildUserAgent();

    /// <summary>
    /// The backoff sleep between retry attempts. A seam so tests can advance
    /// without real delay; production uses <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; set; } = Task.Delay;

    /// <summary>Create a client. See <see cref="VerifiablClientOptions"/>.</summary>
    public VerifiablClient(VerifiablClientOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _auth = options.Auth
            ?? throw new ArgumentException(
                "Auth is required: pass VerifiablAuth.ClientCredentials(...) or VerifiablAuth.ApiKey(...).",
                nameof(options));

        VerifiablEnvironment environment = VerifiablEndpoints.Validate(
            options.Environment,
            $"{nameof(options)}.{nameof(options.Environment)}");

        Uri? tokenUrlOverride = (_auth as VerifiablAuth.ClientCredentialsAuth)?.TokenUrl;
        _tokenUrl = tokenUrlOverride is null
            ? VerifiablEndpoints.TokenUrlFor(environment)
            : ValidateTokenUrl(tokenUrlOverride);

        _issuerBaseUrl = options.IssuerBaseUrl is null
            ? VerifiablEndpoints.IssuerBaseUrlFor(environment)
            : ValidateIssuerBaseUrl(options.IssuerBaseUrl);

        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Timeout must be positive.",
                $"{nameof(options)}.{nameof(options.Timeout)}");
        }

        if (options.MaxRetries < 0)
        {
            throw new ArgumentException(
                "MaxRetries must not be negative.",
                $"{nameof(options)}.{nameof(options.MaxRetries)}");
        }

        _timeout = options.Timeout;
        _maxRetries = options.MaxRetries;
        _httpClient = options.HttpClient ?? SharedHttpClient.Value;
        _onRequest = options.OnRequest;
        _onResponse = options.OnResponse;
        _onError = options.OnError;
    }

    /// <summary>
    /// Register non-PII payslip data and decryption metadata. Returns the
    /// Verifiabl reference to embed in a locally generated barcode.
    /// </summary>
    /// <exception cref="VerifiablApiException">The API returned a non-2xx response.</exception>
    /// <exception cref="VerifiablAuthException">An OAuth token could not be obtained.</exception>
    /// <exception cref="TimeoutException">The call exceeded the configured timeout.</exception>
    public virtual Task<RegisterNonPiiResponse> RegisterNonPiiAsync(
        RegisterNonPiiRequest request,
        CancellationToken cancellationToken = default)
    {
        JsonObject body = Wire.ToWire(request);
        // Single registration: Verifiabl generates the reference and does not
        // deduplicate, so an ambiguous retry could create a second record. Only
        // failures that leave the request unprocessed are retried.
        return PostAsync(
            "/v1/registerNonPII",
            body,
            Wire.RegistrationFromWire,
            idempotent: false,
            cancellationToken);
    }

    /// <summary>
    /// Register non-PII payslip data and have the API build the barcode. Sends the
    /// encrypted PII alongside the non-PII data.
    /// </summary>
    /// <exception cref="VerifiablApiException">The API returned a non-2xx response.</exception>
    /// <exception cref="VerifiablAuthException">An OAuth token could not be obtained.</exception>
    /// <exception cref="TimeoutException">The call exceeded the configured timeout.</exception>
    public virtual Task<CreateBarcodeResponse> CreateBarcodeAsync(
        CreateBarcodeRequest request,
        CancellationToken cancellationToken = default)
    {
        JsonObject body = Wire.ToWire(request);
        // Same as RegisterNonPiiAsync: the API assigns the reference, so this is
        // not safe to retry on an ambiguous failure.
        return PostAsync(
            "/v1/registerAndBuildSymbol",
            body,
            Wire.CreateBarcodeFromWire,
            idempotent: false,
            cancellationToken);
    }

    /// <summary>
    /// Register a batch of non-PII payslip records in a single request, up to
    /// <see cref="MaxBatchRecords"/> records. Each record carries a
    /// provider-generated Verifiabl reference (from
    /// <see cref="VerifiablReference.Generate"/>) and the same fields as
    /// <see cref="RegisterNonPiiAsync"/>. The response contains a per-record
    /// result index-aligned to the input: one bad record never fails the batch.
    /// </summary>
    /// <exception cref="VerifiablApiException">The API returned a non-2xx response.</exception>
    /// <exception cref="VerifiablAuthException">An OAuth token could not be obtained.</exception>
    /// <exception cref="TimeoutException">The call exceeded the configured timeout.</exception>
    public virtual Task<RegisterNonPiiBatchResponse> RegisterNonPiiBatchAsync(
        IEnumerable<BatchRecord> records,
        CancellationToken cancellationToken = default)
    {
        if (records is null)
        {
            throw new ArgumentNullException(nameof(records));
        }

        JsonObject body = Wire.ToWire(records.ToList());
        // Batch records carry provider-generated references, which the API treats
        // as idempotency keys: re-sending returns "duplicate" for stored rows and
        // writes only the missing ones. So a transient failure is safe to retry.
        return PostAsync(
            "/v1/registerNonPIIBatch",
            body,
            Wire.BatchFromWire,
            idempotent: true,
            cancellationToken);
    }

    private async Task<T> PostAsync<T>(
        string path,
        JsonObject body,
        Func<JsonElement, T> parseResponse,
        bool idempotent,
        CancellationToken cancellationToken)
    {
        string json = body.ToJsonString();

        // One deadline covers the whole operation: token fetches, requests, the
        // 401 refresh, and every transient retry with its backoff.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);

        try
        {
            int attempt = 0;
            while (true)
            {
                HttpResponseMessage response;
                try
                {
                    response = await SendWithAuthAsync(path, json, deadline.Token)
                        .ConfigureAwait(false);
                }
                catch (HttpRequestException) when (idempotent && attempt < _maxRetries)
                {
                    // A network fault before any response. Safe to retry only for
                    // idempotent calls, where a re-send cannot duplicate work.
                    attempt++;
                    await DelayAsync(BackoffDelay(attempt), deadline.Token).ConfigureAwait(false);
                    continue;
                }

                if (attempt < _maxRetries
                    && IsRetryableStatus((int)response.StatusCode, idempotent))
                {
                    TimeSpan delay = RetryAfterOrBackoff(response, attempt + 1);
                    response.Dispose();
                    attempt++;
                    await DelayAsync(delay, deadline.Token).ConfigureAwait(false);
                    continue;
                }

                using (response)
                {
                    string text = await ReadBodyAsync(response).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new VerifiablApiException(
                            (int)response.StatusCode,
                            ParseErrorBody(text),
                            ExtractRequestId(response));
                    }

                    using JsonDocument document = ParseJsonBody(text, (int)response.StatusCode);
                    return parseResponse(document.RootElement);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The Verifiabl API call to {path} did not complete within " +
                $"{_timeout.TotalSeconds:0.###} seconds.");
        }
    }

    /// <summary>
    /// Send one request, transparently refreshing the OAuth token and retrying
    /// exactly once on a 401. Transient-status and network retries are layered on
    /// top of this by <see cref="PostAsync"/>.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithAuthAsync(
        string path,
        string json,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await SendAsync(path, json, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
            && _auth is VerifiablAuth.ClientCredentialsAuth)
        {
            // The cached token may have been revoked or expired early; fetch a
            // fresh one and retry exactly once.
            _tokenCache = null;
            response.Dispose();
            response = await SendAsync(path, json, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendAsync(
        string path,
        string json,
        CancellationToken cancellationToken)
    {
        string url = _issuerBaseUrl + path;
        var stopwatch = Stopwatch.StartNew();
        string token = await GetBearerTokenAsync(cancellationToken).ConfigureAwait(false);
        CallHook(_onRequest, new VerifiablRequestEvent(url, path));

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CallHook(
                _onError,
                new VerifiablErrorEvent(url, path, stopwatch.Elapsed.TotalMilliseconds, exception));
            throw;
        }

        CallHook(_onResponse, new VerifiablResponseEvent(
            url,
            path,
            (int)response.StatusCode,
            stopwatch.Elapsed.TotalMilliseconds,
            ExtractRequestId(response)));
        return response;
    }

    private async Task<string> GetBearerTokenAsync(CancellationToken cancellationToken)
    {
        if (_auth is VerifiablAuth.ApiKeyAuth apiKey)
        {
            return apiKey.Key;
        }

        CachedToken? cached = _tokenCache;
        if (cached is not null && cached.IsReusable())
        {
            return cached.AccessToken;
        }

        // Single-flight: concurrent callers queue here, and each re-checks the
        // cache after acquiring so only one token request goes out.
        await _tokenSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = _tokenCache;
            if (cached is not null && cached.IsReusable())
            {
                return cached.AccessToken;
            }

            CachedToken fresh = await RequestAccessTokenAsync(cancellationToken)
                .ConfigureAwait(false);
            _tokenCache = fresh;
            return fresh.AccessToken;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    private async Task<CachedToken> RequestAccessTokenAsync(CancellationToken cancellationToken)
    {
        var credentials = (VerifiablAuth.ClientCredentialsAuth)_auth;
        var body = new JsonObject
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = credentials.ClientId,
            ["client_secret"] = credentials.ClientSecret,
            ["audience"] = _issuerBaseUrl,
            ["scope"] = IssuerScope,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _tokenUrl)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new VerifiablAuthException(
                "Could not reach the Verifiabl OAuth token endpoint.",
                innerException: exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new VerifiablAuthException(
                    $"Verifiabl OAuth token request failed with status {(int)response.StatusCode}.",
                    (int)response.StatusCode);
            }

            string text = await ReadBodyAsync(response).ConfigureAwait(false);
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text);
            }
            catch (JsonException)
            {
                throw new VerifiablAuthException("Verifiabl OAuth token response was not valid JSON.");
            }

            using (document)
            {
                Wire.TokenResponse? token = Wire.TokenFromWire(document.RootElement);
                if (token is null)
                {
                    throw new VerifiablAuthException(
                        "Verifiabl OAuth token response had an unexpected shape.");
                }

                DateTimeOffset issuedAt = DateTimeOffset.UtcNow;
                return new CachedToken(
                    token.AccessToken,
                    issuedAt,
                    issuedAt + TimeSpan.FromSeconds(token.ExpiresInSeconds));
            }
        }
    }

    private const double RetryBaseDelaySeconds = 0.5;
    private const double RetryMaxDelaySeconds = 8.0;
    private static readonly Random JitterRandom = new();

    private static string BuildUserAgent()
    {
        Version? version = typeof(VerifiablClient).Assembly.GetName().Version;
        string v = version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        return $"verifiabl-dotnet/{v} ({RuntimeInformation.FrameworkDescription})";
    }

    private static bool IsRetryableStatus(int status, bool idempotent)
    {
        // 429 (throttled) and 503 (unavailable) mean the request was not
        // processed, so they are safe to retry even for a non-idempotent single
        // registration.
        if (status == 429 || status == 503)
        {
            return true;
        }

        // Other 5xx and 408 are ambiguous: the server may have processed the
        // request before failing, which would duplicate a single registration.
        // Retry them only when the call is idempotent.
        return idempotent && (status == 408 || (status >= 500 && status <= 599));
    }

    private static TimeSpan RetryAfterOrBackoff(HttpResponseMessage response, int attempt)
    {
        RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is TimeSpan delta && delta > TimeSpan.Zero)
            {
                return CapDelay(delta);
            }

            if (retryAfter.Date is DateTimeOffset date)
            {
                TimeSpan until = date - DateTimeOffset.UtcNow;
                if (until > TimeSpan.Zero)
                {
                    return CapDelay(until);
                }
            }
        }

        return BackoffDelay(attempt);
    }

    private static TimeSpan BackoffDelay(int attempt)
    {
        // Exponential backoff with equal jitter: half fixed, half random, so
        // concurrent retries neither thunder together nor collapse to zero.
        double capped = Math.Min(
            RetryMaxDelaySeconds,
            RetryBaseDelaySeconds * Math.Pow(2, attempt - 1));
        double jitter;
        lock (JitterRandom)
        {
            jitter = JitterRandom.NextDouble();
        }

        return TimeSpan.FromSeconds(capped / 2 + (capped / 2 * jitter));
    }

    private static TimeSpan CapDelay(TimeSpan delay)
    {
        TimeSpan max = TimeSpan.FromSeconds(RetryMaxDelaySeconds);
        return delay > max ? max : delay;
    }

    private static HttpClient CreateSharedHttpClient()
    {
#if NET8_0_OR_GREATER
        // Recycle pooled connections so long-lived processes pick up DNS changes.
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        var client = new HttpClient(handler);
#else
        var client = new HttpClient();
#endif
        // The SDK applies its own per-call deadline.
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response)
    {
        if (response.Content is null)
        {
            return string.Empty;
        }

        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private static JsonDocument ParseJsonBody(string text, int status)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException(
                $"Verifiabl API returned an empty response body with status {status}.");
        }

        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw new FormatException($"Verifiabl API returned invalid JSON with status {status}.");
        }
    }

    private static VerifiablErrorBody? ParseErrorBody(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return Wire.ErrorBodyFromWire(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractRequestId(HttpResponseMessage response)
    {
        foreach (string header in (string[])["x-request-id", "request-id", "x-verifiabl-request-id"])
        {
            if (response.Headers.TryGetValues(header, out IEnumerable<string>? values))
            {
                foreach (string value in values)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static void CallHook<T>(Action<T>? hook, T eventArgs)
    {
        try
        {
            hook?.Invoke(eventArgs);
        }
        catch
        {
            // Observability hooks must not change API request behaviour.
        }
    }

    private static string ValidateIssuerBaseUrl(Uri url)
    {
        if (!url.IsAbsoluteUri
            || !(url.Scheme == Uri.UriSchemeHttps
                || (url.Scheme == Uri.UriSchemeHttp && url.IsLoopback)))
        {
            throw new ArgumentException(
                "IssuerBaseUrl must use https, or http for localhost.",
                nameof(VerifiablClientOptions.IssuerBaseUrl));
        }

        return url.GetLeftPart(UriPartial.Authority);
    }

    private static string ValidateTokenUrl(Uri url)
    {
        bool allowed = url.IsAbsoluteUri
            && ((url.Scheme == Uri.UriSchemeHttps
                    && Array.IndexOf(VerifiablAuthHosts, url.Host) >= 0)
                || ((url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
                    && url.IsLoopback));
        if (!allowed)
        {
            throw new ArgumentException(
                "tokenUrl must use a Verifiabl auth host, or localhost for development.",
                "tokenUrl");
        }

        return url.AbsoluteUri;
    }

    private sealed class CachedToken
    {
        internal CachedToken(string accessToken, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
        {
            AccessToken = accessToken;
            IssuedAt = issuedAt;
            ExpiresAt = expiresAt;
        }

        internal string AccessToken { get; }

        internal DateTimeOffset IssuedAt { get; }

        internal DateTimeOffset ExpiresAt { get; }

        internal bool IsReusable()
        {
            TimeSpan ttl = ExpiresAt - IssuedAt;
            TimeSpan refreshBuffer = TimeSpan.FromTicks(
                Math.Min(MaxTokenRefreshBuffer.Ticks, ttl.Ticks / 2));
            return ExpiresAt - DateTimeOffset.UtcNow > refreshBuffer;
        }
    }
}
