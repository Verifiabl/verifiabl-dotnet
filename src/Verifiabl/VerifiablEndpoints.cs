namespace Verifiabl;

/// <summary>Issuer API and public scan URL origins for each Verifiabl environment.</summary>
public static class VerifiablEndpoints
{
    /// <summary>Production issuer API origin used by issuing integrations.</summary>
    public const string ProductionIssuerBaseUrl = "https://register.verifiabl.io";

    /// <summary>Production origin of the public QR scan URL printed on documents.</summary>
    public const string ProductionScanBaseUrl = "https://verify.verifiabl.io";

    /// <summary>Production root origin used only by opt-in v2 barcode writers.</summary>
    public const string ProductionV2ScanBaseUrl = "https://verifiabl.io";

    /// <summary>Sandbox issuer API origin, selected via <see cref="VerifiablEnvironment.Sandbox"/>.</summary>
    public const string SandboxIssuerBaseUrl = "https://register.sandbox.verifiabl.io";

    /// <summary>Sandbox origin of the public QR scan URL.</summary>
    public const string SandboxScanBaseUrl = "https://verify.sandbox.verifiabl.io";

    /// <summary>Sandbox root origin used only by opt-in v2 barcode writers.</summary>
    public const string SandboxV2ScanBaseUrl = "https://sandbox.verifiabl.io";

    internal const string ProductionTokenUrl = "https://auth.verifiabl.io/oauth/token";
    internal const string SandboxTokenUrl = "https://auth.sandbox.verifiabl.io/oauth/token";

    internal static string IssuerBaseUrlFor(VerifiablEnvironment environment) =>
        environment == VerifiablEnvironment.Sandbox ? SandboxIssuerBaseUrl : ProductionIssuerBaseUrl;

    internal static string ScanBaseUrlFor(VerifiablEnvironment environment) =>
        environment == VerifiablEnvironment.Sandbox ? SandboxScanBaseUrl : ProductionScanBaseUrl;

    internal static string V2ScanBaseUrlFor(VerifiablEnvironment environment) =>
        environment == VerifiablEnvironment.Sandbox
            ? SandboxV2ScanBaseUrl
            : ProductionV2ScanBaseUrl;

    internal static string TokenUrlFor(VerifiablEnvironment environment) =>
        environment == VerifiablEnvironment.Sandbox ? SandboxTokenUrl : ProductionTokenUrl;

    internal static VerifiablEnvironment Validate(VerifiablEnvironment environment, string paramName)
    {
        if (environment != VerifiablEnvironment.Production && environment != VerifiablEnvironment.Sandbox)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                environment,
                "Environment must be Production or Sandbox.");
        }

        return environment;
    }
}
