namespace Verifiabl.Client;

/// <summary>
/// How the client authenticates to the Verifiabl API.
/// </summary>
/// <remarks>
/// Pass the OAuth2 client ID and secret issued during onboarding via
/// <see cref="ClientCredentials"/> and the client fetches, caches, and refreshes
/// access tokens automatically.
/// </remarks>
public abstract class VerifiablAuth
{
    private protected VerifiablAuth()
    {
    }

    /// <summary>
    /// OAuth2 client credentials issued by Verifiabl during onboarding.
    /// </summary>
    /// <param name="clientId">OAuth client ID.</param>
    /// <param name="clientSecret">
    /// OAuth client secret. Load from a secrets manager; never hard-code it.
    /// </param>
    /// <param name="tokenUrl">
    /// OAuth token endpoint (default: the environment's auth service, e.g.
    /// https://auth.verifiabl.io/oauth/token). Overrides must use a Verifiabl
    /// auth host, or localhost for local development.
    /// </param>
    public static VerifiablAuth ClientCredentials(
        string clientId,
        string clientSecret,
        Uri? tokenUrl = null)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new ArgumentException("clientId and clientSecret are required.");
        }

        return new ClientCredentialsAuth(clientId.Trim(), clientSecret.Trim(), tokenUrl);
    }

    internal sealed class ClientCredentialsAuth : VerifiablAuth
    {
        internal ClientCredentialsAuth(string clientId, string clientSecret, Uri? tokenUrl)
        {
            ClientId = clientId;
            ClientSecret = clientSecret;
            TokenUrl = tokenUrl;
        }

        internal string ClientId { get; }

        internal string ClientSecret { get; }

        internal Uri? TokenUrl { get; }
    }
}
