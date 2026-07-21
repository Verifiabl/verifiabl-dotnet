using System.Text.Json.Nodes;

namespace Verifiabl;

/// <summary>
/// Non-PII payslip data. <see cref="PeriodStart"/> and <see cref="PeriodEnd"/>
/// are required (YYYY-MM-DD).
/// </summary>
/// <remarks>
/// <see cref="AdditionalData"/> fields are passed through to the API verbatim,
/// under the exact keys you supply: only the period fields are SDK-defined and
/// translated to the wire names. Provider-specific fields (e.g. line items) use
/// whatever names your payslip schema specifies, which are typically snake_case
/// on the wire.
/// </remarks>
public sealed class PayslipNonPii
{
    /// <summary>First day of the pay period, YYYY-MM-DD.</summary>
    public string? PeriodStart { get; set; }

    /// <summary>Last day of the pay period, YYYY-MM-DD.</summary>
    public string? PeriodEnd { get; set; }

    /// <summary>Provider-specific payslip fields, sent to the API verbatim.</summary>
    public JsonObject? AdditionalData { get; set; }
}
