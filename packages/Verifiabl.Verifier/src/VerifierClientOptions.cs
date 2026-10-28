using System.Net.Http;

namespace Verifiabl.Verifier;

/// <summary>Options for constructing a <see cref="VerifierClient"/>.</summary>
public sealed class VerifierClientOptions
{
    /// <summary>How to authenticate. Required. See <see cref="VerifierAuth"/>.</summary>
    public VerifierAuth? Auth { get; set; }

    /// <summary>API environment. Defaults to production.</summary>
    public VerifierEnvironment Environment { get; set; } = VerifierEnvironment.Production;

    /// <summary>
    /// Verification API origin override for local development. Most integrations
    /// leave this unset and use <see cref="Environment"/>. Must use https, except
    /// localhost may use http.
    /// </summary>
    public Uri? VerifierBaseUrl { get; set; }

    /// <summary>
    /// Deadline for each verification, including token fetches
    /// and retries. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum automatic retries after the first attempt. Defaults to 2; set 0 to
    /// disable. Rate limits, timeouts, 5xx responses and network faults are
    /// retried with exponential backoff, honouring <c>Retry-After</c>.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// The <see cref="System.Net.Http.HttpClient"/> to send requests with. The SDK
    /// never disposes it. When unset, a shared internal client is used. The SDK
    /// applies its own deadline, so leave the client's Timeout at infinite.
    /// </summary>
    public HttpClient? HttpClient { get; set; }
}
