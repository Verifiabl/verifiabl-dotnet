namespace Verifiabl.Verifier.Internal;

/// <summary>Verification API and OAuth origins for each environment.</summary>
internal static class Endpoints
{
    internal const string ProductionVerifierBaseUrl = "https://verify.verifiabl.io";
    internal const string SandboxVerifierBaseUrl = "https://verify.sandbox.verifiabl.io";
    internal const string ProductionTokenUrl = "https://auth.verifiabl.io/oauth/token";
    internal const string SandboxTokenUrl = "https://auth.sandbox.verifiabl.io/oauth/token";

    internal static string VerifierBaseUrlFor(VerifierEnvironment environment) =>
        environment == VerifierEnvironment.Sandbox ? SandboxVerifierBaseUrl : ProductionVerifierBaseUrl;

    internal static string TokenUrlFor(VerifierEnvironment environment) =>
        environment == VerifierEnvironment.Sandbox ? SandboxTokenUrl : ProductionTokenUrl;

    internal static VerifierEnvironment Validate(VerifierEnvironment environment, string paramName)
    {
        if (environment != VerifierEnvironment.Production && environment != VerifierEnvironment.Sandbox)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                environment,
                "Environment must be Production or Sandbox.");
        }

        return environment;
    }
}
