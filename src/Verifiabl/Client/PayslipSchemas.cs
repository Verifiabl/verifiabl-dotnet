namespace Verifiabl.Client;

/// <summary>Canonical payslip schema identifiers accepted by the issuer API.</summary>
public static class PayslipSchemas
{
    /// <summary>Frozen Australian v1 schema.</summary>
    public const string AustralianV1 = "au.payslip.v1";

    /// <summary>Australian v2 schema.</summary>
    public const string AustralianV2 = "au.payslip.v2";

    /// <summary>Frozen New Zealand v1 schema.</summary>
    public const string NewZealandV1 = "nz.payslip.v1";

    /// <summary>New Zealand v2 schema.</summary>
    public const string NewZealandV2 = "nz.payslip.v2";
}
