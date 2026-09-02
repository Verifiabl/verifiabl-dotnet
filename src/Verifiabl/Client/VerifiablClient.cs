using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Verifiabl.Internal;

namespace Verifiabl.Client;

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
/// Every failure an API call reports derives from <see cref="VerifiablException"/>,
/// so one <c>catch (VerifiablException)</c> covers API errors, auth failures,
/// timeouts, and transport faults.
/// </para>
/// <para>
/// Depend on <see cref="IVerifiablClient"/> where you need to substitute a fake.
/// </para>
/// </remarks>
public sealed class VerifiablClient : IVerifiablClient
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

        ValidateOptions(options);

        _auth = options.Auth!;

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

        _timeout = options.Timeout;
        _maxRetries = options.MaxRetries;
        _httpClient = options.HttpClient ?? SharedHttpClient.Value;
        _onRequest = options.OnRequest;
        _onResponse = options.OnResponse;
        _onError = options.OnError;
    }

    internal static void ValidateOptions(VerifiablClientOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        VerifiablAuth auth = options.Auth
            ?? throw new ArgumentException(
                "Auth is required: pass VerifiablAuth.ClientCredentials(...) or VerifiablAuth.ApiKey(...).",
                nameof(options));

        VerifiablEndpoints.Validate(
            options.Environment,
            $"{nameof(options)}.{nameof(options.Environment)}");

        Uri? tokenUrlOverride = (auth as VerifiablAuth.ClientCredentialsAuth)?.TokenUrl;
        if (tokenUrlOverride is not null)
        {
            ValidateTokenUrl(tokenUrlOverride);
        }

        if (options.IssuerBaseUrl is not null)
        {
            ValidateIssuerBaseUrl(options.IssuerBaseUrl);
        }

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
    }

    /// <inheritdoc />
    public Task<RegisterNonPiiResponse> RegisterNonPiiAsync(
        RegisterNonPiiRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // The reference is minted client-side (or taken from the request), which
        // puts the API on its idempotent path: replaying the same reference with
        // identical content returns the stored record as "duplicate" instead of
        // creating a second one, so ambiguous failures are safe to retry.
        string reference = request.VerifiablReference is null
            ? Verifiabl.VerifiablReference.Generate()
            : Verifiabl.VerifiablReference.Validate(
                request.VerifiablReference,
                $"{nameof(request)}.{nameof(request.VerifiablReference)}");
        JsonObject body = Wire.ToWire(request, reference);
        return PostAsync(
            "/v1/registerNonPII",
            body,
            Wire.RegistrationFromWire,
            idempotent: true,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RegisterAndBuildBarcodeResponse> RegisterAndBuildBarcodeAsync(
        RegisterAndBuildBarcodeRequest request,
        CancellationToken cancellationToken = default)
    {
        JsonObject body = Wire.ToWire(request);
        // The API mints this endpoint's reference and cannot deduplicate a
        // resend, so only failures enforced before processing (429) are retried.
        return PostAsync(
            "/v1/registerAndBuildBarcode",
            body,
            Wire.RegisterAndBuildBarcodeFromWire,
            idempotent: false,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RegisterNonPiiBatchResponse> RegisterNonPiiBatchAsync(
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
                        throw VerifiablApiException.FromResponse(
                            (int)response.StatusCode,
                            ParseErrorBody(text),
                            ExtractRequestId(response));
                    }

                    using JsonDocument document = ParseJsonBody(text, (int)response.StatusCode);
                    try
                    {
                        return parseResponse(document.RootElement);
                    }
                    catch (FormatException exception)
                    {
                        throw new VerifiablTransportException(exception.Message, exception);
                    }
                }
            }
        }
        catch (HttpRequestException exception)
        {
            throw new VerifiablTransportException(
                $"The Verifiabl API call to {path} failed with a transport error: {exception.Message}",
                exception);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new VerifiablTimeoutException(
                $"The Verifiabl API call to {path} did not complete within " +
                $"{_timeout.TotalSeconds:0.###} seconds.",
                _timeout);
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
        (HttpResponseMessage response, string bearerToken) =
            await SendAsync(path, json, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
            && _auth is VerifiablAuth.ClientCredentialsAuth)
        {
            // The cached token may have been revoked or expired early; fetch a
            // fresh one and retry exactly once. Only drop the cache if it still
            // holds the rejected token, so a concurrent refresh isn't wiped.
            CachedToken? current = _tokenCache;
            if (current is not null && current.AccessToken == bearerToken)
            {
                _tokenCache = null;
            }

            response.Dispose();
            (response, _) = await SendAsync(path, json, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private async Task<(HttpResponseMessage Response, string BearerToken)> SendAsync(
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
        return (response, token);
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
        // Rate limits are enforced before any processing (at the edge and in
        // pre-controller middleware), so 429 is safe to retry without
        // idempotency.
        if (status == 429)
        {
            return true;
        }

        // Everything else — 503 included — can arrive after the server has
        // committed the write (a connection lost in the commit-ack window, or a
        // platform 503 wrapping a completed request), so it needs idempotency.
        return idempotent && (status == 408 || (status >= 500 && status <= 599));
    }

    private static TimeSpan RetryAfterOrBackoff(HttpResponseMessage response, int attempt)
    {
        // A server-provided Retry-After is honoured in full (the overall deadline
        // still bounds the wait); only the computed backoff is capped.
        RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is TimeSpan delta && delta > TimeSpan.Zero)
            {
                return delta;
            }

            if (retryAfter.Date is DateTimeOffset date)
            {
                TimeSpan until = date - DateTimeOffset.UtcNow;
                if (until > TimeSpan.Zero)
                {
                    return until;
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
            throw new VerifiablTransportException(
                $"Verifiabl API returned an empty response body with status {status}.");
        }

        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new VerifiablTransportException(
                $"Verifiabl API returned invalid JSON with status {status}.",
                exception);
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

    private static readonly string[] RequestIdHeaders =
        ["x-request-id", "request-id", "x-verifiabl-request-id"];

    private static string? ExtractRequestId(HttpResponseMessage response)
    {
        return RequestIdHeaders
            .SelectMany(header =>
                response.Headers.TryGetValues(header, out IEnumerable<string>? values)
                    ? values
                    : Enumerable.Empty<string>())
            .FirstOrDefault();
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
        static ArgumentException Invalid() => new(
            "tokenUrl must use a Verifiabl auth host, or localhost for development.",
            "tokenUrl");

        // Host/IsLoopback throw on a relative Uri, so check this first.
        if (!url.IsAbsoluteUri)
        {
            throw Invalid();
        }

        bool verifiablHost = url.Scheme == Uri.UriSchemeHttps
            && Array.IndexOf(VerifiablAuthHosts, url.Host) >= 0;
        bool loopbackDev = url.IsLoopback
            && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
        if (!verifiablHost && !loopbackDev)
        {
            throw Invalid();
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
