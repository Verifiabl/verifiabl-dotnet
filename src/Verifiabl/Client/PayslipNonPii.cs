#if NET6_0_OR_GREATER
using Verifiabl.Internal;
#endif

namespace Verifiabl.Client;

/// <summary>
/// Non-PII payslip data. <see cref="PeriodEnd"/> is required (YYYY-MM-DD).
/// <see cref="PeriodStart"/> is required for v1 schemas and optional for v2.
/// </summary>
/// <remarks>
/// Use <see cref="FromAustralianV2"/> or <see cref="FromNewZealandV2"/> for
/// v2 payloads. They serialize only generated fields, send each amount as an
/// exact decimal string and each date as YYYY-MM-DD, and require an ISO 4217
/// currency; the API validates the other values.
/// <see cref="AdditionalData"/> is for legacy or future-schema pass-through.
/// </remarks>
public sealed class PayslipNonPii
{
    /// <summary>
    /// First day of the pay period, YYYY-MM-DD. Required for v1 schemas and
    /// omitted for a v2 payslip that prints only a period end.
    /// </summary>
    /// <remarks>
    /// For a typed v2 payload this is a copy of the payload's date. The request
    /// sends the payload, so setting this property has no effect.
    /// </remarks>
    public string? PeriodStart { get; set; }

    /// <summary>Last day of the pay period, YYYY-MM-DD.</summary>
    /// <remarks>
    /// For a typed v2 payload this is a copy of the payload's date. The request
    /// sends the payload, so setting this property has no effect.
    /// </remarks>
    public required string PeriodEnd { get; set; }

    /// <summary>
    /// Free-form non-PII fields for v1 or future schemas, not supported for known v2 profiles.
    /// </summary>
    /// <remarks>
    /// Values may be <see langword="null"/>, <see cref="string"/>,
    /// <see cref="bool"/>, numeric primitives (<see cref="sbyte"/>,
    /// <see cref="byte"/>, <see cref="short"/>, <see cref="ushort"/>,
    /// <see cref="int"/>, <see cref="uint"/>, <see cref="long"/>,
    /// <see cref="ulong"/>, <see cref="float"/>, <see cref="double"/>,
    /// <see cref="decimal"/>), nested string-keyed dictionaries, or sequences
    /// of these values. Other types (including dates and custom objects) throw
    /// an <see cref="ArgumentException"/> naming the offending key.
    /// </remarks>
    public IDictionary<string, object?>? AdditionalData { get; set; }

    internal object? TypedV2Payload { get; private init; }
    internal string? TypedV2Schema { get; private init; }

    /// <summary>Create a typed Australian v2 payload without free-form fields.</summary>
    public static PayslipNonPii FromAustralianV2(AustralianPayslipV2 payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }
        return new PayslipNonPii
        {
            PeriodStart = WireDate(payload.PeriodStart),
            PeriodEnd = WireDate(payload.PeriodEnd),
            TypedV2Payload = payload,
            TypedV2Schema = PayslipSchemas.AustralianV2,
        };
    }

    /// <summary>Create a typed New Zealand v2 payload without free-form fields.</summary>
    public static PayslipNonPii FromNewZealandV2(NewZealandPayslipV2 payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }
        return new PayslipNonPii
        {
            PeriodStart = WireDate(payload.PeriodStart),
            PeriodEnd = WireDate(payload.PeriodEnd),
            TypedV2Payload = payload,
            TypedV2Schema = PayslipSchemas.NewZealandV2,
        };
    }

#if NET6_0_OR_GREATER
    private static string? WireDate(DateOnly? value) => value is { } date ? PayslipDateConverter.Format(date) : null;

    private static string WireDate(DateOnly value) => PayslipDateConverter.Format(value);
#else
    private static T WireDate<T>(T value) => value;
#endif
}
