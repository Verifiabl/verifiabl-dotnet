namespace Verifiabl.Client;

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
    public required string PeriodStart { get; set; }

    /// <summary>Last day of the pay period, YYYY-MM-DD.</summary>
    public required string PeriodEnd { get; set; }

    /// <summary>
    /// Provider-specific payslip fields, sent to the API verbatim.
    /// </summary>
    /// <remarks>
    /// Values may be <see langword="null"/>, <see cref="string"/>,
    /// <see cref="bool"/>, any common numeric type, a nested
    /// <see cref="IDictionary{TKey,TValue}"/> of the same, or a sequence of them.
    /// Anything else throws an <see cref="ArgumentException"/> naming the key.
    /// </remarks>
    public IDictionary<string, object?>? AdditionalData { get; set; }
}
