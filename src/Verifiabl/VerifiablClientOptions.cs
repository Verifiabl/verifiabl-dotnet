using System.Net.Http;

namespace Verifiabl;

/// <summary>Options for constructing a <see cref="VerifiablClient"/>.</summary>
public sealed class VerifiablClientOptions
{
    /// <summary>How to authenticate. Required. See <see cref="VerifiablAuth"/>.</summary>
    public VerifiablAuth? Auth { get; set; }

    /// <summary>API environment. Defaults to production.</summary>
    public VerifiablEnvironment Environment { get; set; } = VerifiablEnvironment.Production;

    /// <summary>
    /// Advanced local development override for issuer API calls. Most integrations
    /// should leave this unset and use <see cref="Environment"/> instead. Must use
    /// https, except localhost may use http.
    /// </summary>
    public Uri? IssuerBaseUrl { get; set; }

    /// <summary>Timeout applied to each API call. Defaults to 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The <see cref="System.Net.Http.HttpClient"/> to send requests with. Supply
    /// one from IHttpClientFactory in long-running services; the SDK never
    /// disposes it. When unset, a shared internal client is used. The SDK manages
    /// its own per-request timeouts, so the supplied client's Timeout should be
    /// left at its default or set to infinite.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>Called before each Verifiabl API request. Bodies are not included.</summary>
    public Action<VerifiablRequestEvent>? OnRequest { get; set; }

    /// <summary>Called after each Verifiabl API response. Bodies are not included.</summary>
    public Action<VerifiablResponseEvent>? OnResponse { get; set; }

    /// <summary>Called when an issuer API request fails before receiving a response.</summary>
    public Action<VerifiablErrorEvent>? OnError { get; set; }
}

/// <summary>Details of an outgoing issuer API request, for observability hooks.</summary>
public class VerifiablRequestEvent
{
    internal VerifiablRequestEvent(string url, string path)
    {
        Url = url;
        Path = path;
    }

    /// <summary>HTTP method. Always "POST".</summary>
    public string Method => "POST";

    /// <summary>Full request URL.</summary>
    public string Url { get; }

    /// <summary>Request path, e.g. "/v1/registerNonPII".</summary>
    public string Path { get; }
}

/// <summary>Details of a completed issuer API response, for observability hooks.</summary>
public sealed class VerifiablResponseEvent : VerifiablRequestEvent
{
    internal VerifiablResponseEvent(
        string url,
        string path,
        int status,
        double elapsedMs,
        string? requestId)
        : base(url, path)
    {
        Status = status;
        ElapsedMs = elapsedMs;
        RequestId = requestId;
    }

    /// <summary>HTTP status code.</summary>
    public int Status { get; }

    /// <summary>Elapsed time in milliseconds.</summary>
    public double ElapsedMs { get; }

    /// <summary>Request ID returned by the API, when present.</summary>
    public string? RequestId { get; }
}

/// <summary>Details of a failed issuer API request, for observability hooks.</summary>
public sealed class VerifiablErrorEvent : VerifiablRequestEvent
{
    internal VerifiablErrorEvent(string url, string path, double elapsedMs, Exception error)
        : base(url, path)
    {
        ElapsedMs = elapsedMs;
        Error = error;
    }

    /// <summary>Elapsed time in milliseconds.</summary>
    public double ElapsedMs { get; }

    /// <summary>The exception that failed the request.</summary>
    public Exception Error { get; }
}
