#if NET472
using System.Net;
#endif
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Verifiabl.Verifier.Internal;

/// <summary>
/// Sends authenticated JSON requests to the verification API. It fetches,
/// caches and refreshes OAuth tokens, retries a 401 once with a fresh token,
/// retries transient failures and applies one deadline to the whole call. It is
/// thread-safe.
/// </summary>
/// <remarks>
/// Adapted from Verifiabl.Issuer's client. Keep retry and token behaviour in
/// step with it. Retries can create additional verification events; invoice-time
/// rating policy, not this transport, determines their charges.
/// </remarks>
internal sealed class ApiTransport
{
    internal static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(2);

    private const string VerifierScope = "verifiabl:verifier";

    private static readonly string[] VerifiablAuthHosts =
    [
        "auth.verifiabl.io",
        "auth.sandbox.verifiabl.io",
    ];

    /// <summary>Maximum time before expiry that an OAuth token is treated as stale.</summary>
    private static readonly TimeSpan MaxTokenRefreshBuffer = TimeSpan.FromSeconds(60);

    private static readonly Lazy<HttpClient> SharedHttpClient = new(CreateSharedHttpClient);

    private static readonly string[] RequestIdHeaders =
        ["x-request-id", "request-id", "x-verifiabl-request-id"];

    /// <summary>
    /// Sent so Verifiabl support can identify the SDK version behind a call. It
    /// carries no usage telemetry.
    /// </summary>
    private static readonly string UserAgent = BuildUserAgent();

    private const double RetryBaseDelaySeconds = 0.5;
    private const double RetryMaxDelaySeconds = 8.0;
#if !NET6_0_OR_GREATER
    private static readonly Random JitterRandom = new();
#endif

    private readonly VerifierAuth.ClientCredentialsAuth _credentials;
    private readonly string _tokenUrl;
    private readonly string _baseUrl;
    private readonly TimeSpan _timeout;
    private readonly int _maxRetries;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private volatile CachedToken? _tokenCache;

    internal ApiTransport(VerifierClientOptions options)
    {
        Validate(options);

        _credentials = (VerifierAuth.ClientCredentialsAuth)options.Auth!;
        _tokenUrl = TokenUrlFor(_credentials, options.Environment);
        _baseUrl = options.VerifierBaseUrl is null
            ? Endpoints.VerifierBaseUrlFor(options.Environment)
            : ValidateBaseUrl(options.VerifierBaseUrl);
        _timeout = options.Timeout;
        _maxRetries = options.MaxRetries;
        _httpClient = options.HttpClient ?? SharedHttpClient.Value;
#if NET472
        if (options.HttpClient is null)
        {
            // The shared transport also serves caller-specified development origins.
            ConfigureConnectionLease(new Uri(_baseUrl));
            ConfigureConnectionLease(new Uri(_tokenUrl));
        }
#endif
    }

    /// <summary>
    /// The backoff sleep between retry attempts. A seam so tests can advance
    /// without real delay; production uses <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; set; } = Task.Delay;

    internal static void Validate(VerifierClientOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        VerifierAuth auth = options.Auth
            ?? throw new ArgumentException(
                "Auth is required: pass VerifierAuth.ClientCredentials(...).",
                nameof(options));

        Endpoints.Validate(options.Environment, $"{nameof(options)}.{nameof(options.Environment)}");
        TokenUrlFor((VerifierAuth.ClientCredentialsAuth)auth, options.Environment);

        if (options.VerifierBaseUrl is not null)
        {
            ValidateBaseUrl(options.VerifierBaseUrl);
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

    /// <summary>
    /// POST <paramref name="body"/> to <paramref name="path"/> and parse a 2xx
    /// response. A non-2xx response throws a <see cref="VerifierException"/>.
    /// </summary>
    internal async Task<T> PostAsync<T>(
        string path,
        JsonObject body,
        Func<JsonElement, T> parseResponse,
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
                    response = await SendWithAuthAsync(path, json, deadline.Token).ConfigureAwait(false);
                }
                catch (HttpRequestException) when (attempt < _maxRetries)
                {
                    attempt++;
                    await DelayAsync(BackoffDelay(attempt), deadline.Token).ConfigureAwait(false);
                    continue;
                }

                if (attempt < _maxRetries && IsRetryableStatus((int)response.StatusCode))
                {
                    TimeSpan delay = RetryAfterOrBackoff(response, attempt + 1);
                    response.Dispose();
                    attempt++;
                    await DelayAsync(delay, deadline.Token).ConfigureAwait(false);
                    continue;
                }

                using (response)
                {
                    return await ReadResponseAsync(response, parseResponse, deadline.Token).ConfigureAwait(false);
                }
            }
        }
        catch (HttpRequestException exception)
        {
            throw new VerifierTransportException(
                $"The Verifiabl API call to {path} failed with a transport error: {exception.Message}",
                exception);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimedOut();
        }
    }

    private VerifierTimeoutException TimedOut() =>
        new($"The Verifiabl verification did not complete within {_timeout.TotalSeconds:0.###} seconds.", _timeout);

    private static async Task<T> ReadResponseAsync<T>(
        HttpResponseMessage response,
        Func<JsonElement, T> parseResponse,
        CancellationToken cancellationToken)
    {
        string text = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        int status = (int)response.StatusCode;
        string? requestId = ExtractRequestId(response);

        if (!response.IsSuccessStatusCode)
        {
            throw ErrorFromResponse(status, ParseErrorBody(text), requestId, ReadRetryAfter(response));
        }

        using JsonDocument document = ParseJsonBody(text, status, requestId);
        try
        {
            return parseResponse(document.RootElement);
        }
        catch (FormatException exception)
        {
            throw new VerifierTransportException(exception.Message, exception, isRetryable: false)
            {
                Status = status,
                RequestId = requestId,
            };
        }
    }

    private static VerifierException ErrorFromResponse(
        int status,
        VerifierErrorBody? body,
        string? requestId,
        TimeSpan? retryAfter)
    {
        // By status, not code: a gateway can refuse credentials without a Verifiabl body.
        if (status is 401 or 403)
        {
            return new VerifierAuthException($"Verifiabl refused the credentials with status {status}.")
            {
                Status = status,
                Code = body?.Code ?? (status == 401 ? VerifierErrorCodes.Unauthorized : VerifierErrorCodes.Forbidden),
                RequestId = requestId,
                RetryAfter = retryAfter,
            };
        }

        if (body is null || !VerifierErrorCodes.IsRejection(body.Code))
        {
            return VerifierServiceException.FromResponse(status, body, requestId, retryAfter);
        }

        return new VerifierRejectedException(body.Code, body.Error)
        {
            Status = status,
            RequestId = requestId,
            Reason = body.Reason,
            Detail = body.Detail,
            FieldErrors = body.FieldErrors,
        };
    }

    /// <summary>
    /// Send one request, refreshing the OAuth token and retrying exactly once on
    /// a 401. Transient retries are layered on top by <see cref="PostAsync"/>.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithAuthAsync(
        string path,
        string json,
        CancellationToken cancellationToken)
    {
        (HttpResponseMessage response, string bearerToken) =
            await SendAsync(path, json, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            await InvalidateRejectedTokenAsync(bearerToken, cancellationToken).ConfigureAwait(false);
            (response, _) = await SendAsync(path, json, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private async Task InvalidateRejectedTokenAsync(string bearerToken, CancellationToken cancellationToken)
    {
        // Cache publication and invalidation share the lock: a late 401 must
        // not clear a token another caller has just refreshed.
        await _tokenSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CachedToken? current = _tokenCache;
            if (current is not null && current.AccessToken == bearerToken)
            {
                _tokenCache = null;
            }
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    private async Task<(HttpResponseMessage Response, string BearerToken)> SendAsync(
        string path,
        string json,
        CancellationToken cancellationToken)
    {
        string token = await GetBearerTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return (response, token);
    }

    private async Task<string> GetBearerTokenAsync(CancellationToken cancellationToken)
    {
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

            CachedToken fresh = await RequestAccessTokenAsync(cancellationToken).ConfigureAwait(false);
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
        var body = new JsonObject
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _credentials.ClientId,
            ["client_secret"] = _credentials.ClientSecret,
            ["audience"] = _baseUrl,
            ["scope"] = VerifierScope,
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
            throw new VerifierAuthException("Could not reach the Verifiabl OAuth token endpoint.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new VerifierAuthException(
                    $"Verifiabl OAuth token request failed with status {(int)response.StatusCode}.")
                {
                    Status = (int)response.StatusCode,
                    RequestId = ExtractRequestId(response),
                    RetryAfter = ReadRetryAfter(response),
                };
            }

            string text = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            return ParseToken(text, ExtractRequestId(response), ReadRetryAfter(response));
        }
    }

    private static CachedToken ParseToken(string text, string? requestId, TimeSpan? retryAfter)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw new VerifierAuthException("Verifiabl OAuth token response was not valid JSON.")
            {
                RequestId = requestId,
                RetryAfter = retryAfter,
            };
        }

        using (document)
        {
            Wire.TokenResponse token = Wire.TokenFromWire(document.RootElement)
                ?? throw new VerifierAuthException("Verifiabl OAuth token response had an unexpected shape.")
                {
                    RequestId = requestId,
                    RetryAfter = retryAfter,
                };
            DateTimeOffset issuedAt = DateTimeOffset.UtcNow;
            return new CachedToken(
                token.AccessToken,
                issuedAt,
                issuedAt + TimeSpan.FromSeconds(token.ExpiresInSeconds));
        }
    }

    internal static bool IsRetryableStatus(int status) =>
        status == 408 || status == 429 || (status >= 500 && status <= 599);

    private static TimeSpan RetryAfterOrBackoff(HttpResponseMessage response, int attempt)
    {
        // A server-provided Retry-After is honoured in full (the overall deadline
        // still bounds the wait); only the computed backoff is capped.
        return ReadRetryAfter(response) ?? BackoffDelay(attempt);
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is DateTimeOffset date)
        {
            TimeSpan until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return until;
            }
        }

        return null;
    }

    private static TimeSpan BackoffDelay(int attempt)
    {
        // Exponential backoff with equal jitter: half fixed, half random, so
        // concurrent retries neither thunder together nor collapse to zero.
        double capped = Math.Min(RetryMaxDelaySeconds, RetryBaseDelaySeconds * Math.Pow(2, attempt - 1));
#if NET6_0_OR_GREATER
        double jitter = Random.Shared.NextDouble();
#else
        double jitter;
        lock (JitterRandom)
        {
            jitter = JitterRandom.NextDouble();
        }
#endif

        return TimeSpan.FromSeconds(capped / 2 + (capped / 2 * jitter));
    }

    private static string BuildUserAgent()
    {
        Version? version = typeof(ApiTransport).Assembly.GetName().Version;
        string v = version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        return $"verifiabl-verifier-dotnet/{v} ({RuntimeInformation.FrameworkDescription})";
    }

    private static HttpClient CreateSharedHttpClient()
    {
#if NET8_0_OR_GREATER
        // Recycle pooled connections so long-lived processes pick up DNS changes.
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = PooledConnectionLifetime };
        var client = new HttpClient(handler);
#else
        // .NET Framework pools connections through ServicePointManager instead.
        ConfigureConnectionLease(new Uri(Endpoints.ProductionVerifierBaseUrl));
        ConfigureConnectionLease(new Uri(Endpoints.SandboxVerifierBaseUrl));
        ConfigureConnectionLease(new Uri(Endpoints.ProductionTokenUrl));
        ConfigureConnectionLease(new Uri(Endpoints.SandboxTokenUrl));
        var client = new HttpClient();
#endif
        // The SDK applies its own per-call deadline.
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

#if NET472
    private static void ConfigureConnectionLease(Uri uri)
    {
        ServicePointManager.FindServicePoint(uri).ConnectionLeaseTimeout =
            (int)PooledConnectionLifetime.TotalMilliseconds;
    }
#endif

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return string.Empty;
        }

#if NET5_0_OR_GREATER
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        // .NET Framework has no cancellable ReadAsStringAsync overload.
        cancellationToken.ThrowIfCancellationRequested();
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
    }

    private static JsonDocument ParseJsonBody(string text, int status, string? requestId)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new VerifierTransportException(
                $"Verifiabl API returned an empty response body with status {status}.",
                innerException: null,
                isRetryable: false)
            {
                Status = status,
                RequestId = requestId,
            };
        }

        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new VerifierTransportException(
                $"Verifiabl API returned invalid JSON with status {status}.",
                exception,
                isRetryable: false)
            {
                Status = status,
                RequestId = requestId,
            };
        }
    }

    private static VerifierErrorBody? ParseErrorBody(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? Wire.ErrorBodyFromWire(document.RootElement)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractRequestId(HttpResponseMessage response)
    {
        return RequestIdHeaders
            .SelectMany(header =>
                response.Headers.TryGetValues(header, out IEnumerable<string>? values)
                    ? values
                    : Enumerable.Empty<string>())
            .FirstOrDefault();
    }

    private static string TokenUrlFor(VerifierAuth.ClientCredentialsAuth auth, VerifierEnvironment environment) =>
        auth.TokenUrl is null ? Endpoints.TokenUrlFor(environment) : ValidateTokenUrl(auth.TokenUrl);

    private static string ValidateBaseUrl(Uri url)
    {
        if (!url.IsAbsoluteUri
            || !(url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && url.IsLoopback)))
        {
            throw new ArgumentException(
                "VerifierBaseUrl must use https, or http for localhost.",
                $"options.{nameof(VerifierClientOptions.VerifierBaseUrl)}");
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

        bool verifiablHost = url.Scheme == Uri.UriSchemeHttps && Array.IndexOf(VerifiablAuthHosts, url.Host) >= 0;
        bool loopbackDev = url.IsLoopback && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
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
            TimeSpan refreshBuffer = TimeSpan.FromTicks(Math.Min(MaxTokenRefreshBuffer.Ticks, ttl.Ticks / 2));
            return ExpiresAt - DateTimeOffset.UtcNow > refreshBuffer;
        }
    }
}
