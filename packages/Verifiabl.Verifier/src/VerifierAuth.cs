namespace Verifiabl.Verifier;

/// <summary>How a <see cref="VerifierClient"/> authenticates to the Verifiabl API.</summary>
/// <remarks>
/// Pass the OAuth client ID and secret from your lender onboarding to
/// <see cref="ClientCredentials"/>. The client fetches, caches and refreshes
/// access tokens itself.
/// </remarks>
public abstract class VerifierAuth
{
    private protected VerifierAuth()
    {
    }

    /// <summary>OAuth client credentials issued by Verifiabl during lender onboarding.</summary>
    /// <param name="clientId">OAuth client ID.</param>
    /// <param name="clientSecret">OAuth client secret. Keep it out of logs and source control.</param>
    /// <param name="tokenUrl">
    /// Token endpoint override for local development. Must be a Verifiabl auth host
    /// over https, or localhost. Leave unset to use the environment's endpoint.
    /// </param>
    public static VerifierAuth ClientCredentials(string clientId, string clientSecret, Uri? tokenUrl = null)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new ArgumentException("clientId and clientSecret are required.");
        }

        return new ClientCredentialsAuth(clientId.Trim(), clientSecret.Trim(), tokenUrl);
    }

    internal sealed class ClientCredentialsAuth : VerifierAuth
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
