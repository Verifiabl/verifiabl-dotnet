using Verifiabl.Internal;

namespace Verifiabl.Client;

/// <summary>
/// Non-PII payslip data. Create it with <see cref="FromAustralianV2"/>,
/// <see cref="FromNewZealandV2"/> or, for a schema this SDK has no model for,
/// <see cref="ForFutureSchema"/>.
/// </summary>
/// <remarks>
/// The v2 factories serialize only generated fields, send each amount as an
/// exact decimal string and each date as YYYY-MM-DD, and require an ISO 4217
/// currency; the API validates the other values. The recommended
/// <see cref="V2Issuance"/> helpers call them for you. A registration throws an
/// <see cref="ArgumentException"/> before sending when this value was not
/// created for its schema.
/// </remarks>
public sealed class PayslipNonPii
{
    private PayslipNonPii(
        string? periodStart,
        string periodEnd,
        IReadOnlyDictionary<string, object?>? additionalData,
        object? typedV2Payload,
        string? typedV2Schema)
    {
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        AdditionalData = additionalData;
        TypedV2Payload = typedV2Payload;
        TypedV2Schema = typedV2Schema;
    }

    /// <summary>
    /// First day of the pay period, YYYY-MM-DD, or <see langword="null"/> for a
    /// v2 payslip that prints only a period end.
    /// </summary>
    public string? PeriodStart { get; }

    /// <summary>Last day of the pay period, YYYY-MM-DD.</summary>
    public string PeriodEnd { get; }

    /// <summary>
    /// Free-form non-PII fields from <see cref="ForFutureSchema"/>, or
    /// <see langword="null"/> for a v2 payload.
    /// </summary>
    /// <remarks>
    /// A read-only deep copy: nested objects are
    /// <see cref="IReadOnlyDictionary{TKey, TValue}"/> of <see cref="string"/> to
    /// <see cref="object"/>, sequences are <see cref="IReadOnlyList{T}"/> of
    /// <see cref="object"/>, and other values are the scalars you supplied.
    /// </remarks>
    public IReadOnlyDictionary<string, object?>? AdditionalData { get; }

    internal object? TypedV2Payload { get; }
    internal string? TypedV2Schema { get; }

    /// <summary>
    /// Create a typed Australian v2 payload for <see cref="PayslipSchemas.AustralianV2"/>.
    /// The payload is copied, so later changes to it are not sent.
    /// </summary>
    public static PayslipNonPii FromAustralianV2(AustralianPayslipV2 payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }
        AustralianPayslipV2 copy = GeneratedPayslipCopies.Copy(payload);
        return new PayslipNonPii(WireDate(copy.PeriodStart), WireDate(copy.PeriodEnd), null,
            copy, PayslipSchemas.AustralianV2);
    }

    /// <summary>
    /// Create a typed New Zealand v2 payload for <see cref="PayslipSchemas.NewZealandV2"/>.
    /// The payload is copied, so later changes to it are not sent.
    /// </summary>
    public static PayslipNonPii FromNewZealandV2(NewZealandPayslipV2 payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }
        NewZealandPayslipV2 copy = GeneratedPayslipCopies.Copy(payload);
        return new PayslipNonPii(WireDate(copy.PeriodStart), WireDate(copy.PeriodEnd), null,
            copy, PayslipSchemas.NewZealandV2);
    }

    /// <summary>
    /// Create a free-form payload for a schema identifier this SDK has no typed
    /// model for. Registering it as <see cref="PayslipSchemas.AustralianV2"/> or
    /// <see cref="PayslipSchemas.NewZealandV2"/> throws.
    /// </summary>
    /// <param name="periodStart">First day of the pay period, YYYY-MM-DD.</param>
    /// <param name="periodEnd">Last day of the pay period, YYYY-MM-DD.</param>
    /// <param name="additionalData">
    /// Other non-PII fields, sent under the exact keys you supply. They are
    /// validated and deep-copied here, so later changes to your objects are not
    /// sent. Values may be <see langword="null"/>,
    /// <see cref="string"/>, <see cref="bool"/>, numeric primitives
    /// (<see cref="sbyte"/>, <see cref="byte"/>, <see cref="short"/>,
    /// <see cref="ushort"/>, <see cref="int"/>, <see cref="uint"/>,
    /// <see cref="long"/>, <see cref="ulong"/>, <see cref="float"/>,
    /// <see cref="double"/>, <see cref="decimal"/>), nested string-keyed
    /// dictionaries, or sequences of these values. The <c>period_start</c> and
    /// <c>period_end</c> keys are ignored.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="periodStart"/> or <paramref name="periodEnd"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// A value has another type (including dates and custom objects), a key is null
    /// or repeated, or a nested dictionary has a non-string key. The message names
    /// the key.
    /// </exception>
    public static PayslipNonPii ForFutureSchema(
        string periodStart,
        string periodEnd,
        IEnumerable<KeyValuePair<string, object?>>? additionalData = null)
    {
        if (periodStart is null)
        {
            throw new ArgumentNullException(nameof(periodStart));
        }
        if (periodEnd is null)
        {
            throw new ArgumentNullException(nameof(periodEnd));
        }
        IReadOnlyDictionary<string, object?>? fields = additionalData is null
            ? null
            : FreeFormValues.SnapshotFields(additionalData, nameof(additionalData));
        return new PayslipNonPii(periodStart, periodEnd, fields, null, null);
    }

#if NET6_0_OR_GREATER
    private static string? WireDate(DateOnly? value) => value is { } date ? PayslipDateConverter.Format(date) : null;

    private static string WireDate(DateOnly value) => PayslipDateConverter.Format(value);
#else
    private static T WireDate<T>(T value) => value;
#endif
}
