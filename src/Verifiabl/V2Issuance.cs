using Verifiabl.Client;
using Verifiabl.Internal;

namespace Verifiabl;

/// <summary>A locally encrypted v2 issuance, with a persistent self-managed reference.</summary>
public sealed class PreparedV2Payslip
{
    private readonly byte[] _ciphertext;
    private readonly string _schema;
    private readonly DateTimeOffset _issuedAt;
    private readonly PayslipNonPii _payslip;
    private readonly EncryptionMetadata _metadata;

    internal PreparedV2Payslip(string reference, string schema, DateTimeOffset issuedAt,
        PayslipNonPii payslip, EncryptedPii encrypted)
    {
        VerifiablReference = reference;
        _schema = schema;
        _issuedAt = issuedAt;
        _payslip = payslip;
        _metadata = new EncryptionMetadata
        {
            Iv = (byte[])encrypted.Metadata.Iv.Clone(),
            Tag = (byte[])encrypted.Metadata.Tag.Clone(),
        };
        _ciphertext = (byte[])encrypted.Ciphertext.Clone();
    }

    /// <summary>Persist with the registration for self-managed retries across processes.</summary>
    public string VerifiablReference { get; }

    /// <summary>Self-managed request; also suitable for a batch record with this reference.</summary>
    public RegisterNonPiiRequest Registration => new()
    {
        VerifiablReference = VerifiablReference,
        Schema = _schema,
        IssuedAt = _issuedAt,
        PayslipNonPii = CopyPayslip(_payslip),
        EncryptionMetadata = CopyMetadata(),
    };

    /// <summary>The API-managed flow allocates its own reference and cannot retry ambiguous failures.</summary>
    public RegisterAndBuildBarcodeRequest ApiManagedRegistration => new()
    {
        Schema = _schema,
        IssuedAt = _issuedAt,
        PayslipNonPii = CopyPayslip(_payslip),
        EncryptionMetadata = CopyMetadata(),
        EncryptedPii = (byte[])_ciphertext.Clone(),
    };

    // Keep mutable nested lists independent of both the input and every returned request.
    // Copying decimals directly also retains their scale for wire serialization.
    internal static PayslipNonPii CopyPayslip(PayslipNonPii payslip) => payslip.TypedV2Payload switch
    {
        AustralianPayslipV2 au => PayslipNonPii.FromAustralianV2(GeneratedPayslipCopies.Copy(au)),
        NewZealandPayslipV2 nz => PayslipNonPii.FromNewZealandV2(GeneratedPayslipCopies.Copy(nz)),
        _ => throw new ArgumentException("A prepared payslip requires a typed v2 payload.", nameof(payslip)),
    };

    private EncryptionMetadata CopyMetadata() => new()
    {
        Iv = (byte[])_metadata.Iv.Clone(),
        Tag = (byte[])_metadata.Tag.Clone(),
    };

    /// <summary>Pair the registration response reference with this issuance's ciphertext.</summary>
    public BarcodeParts BarcodeParts(string reference) =>
        new(Verifiabl.VerifiablReference.Validate(reference, nameof(reference)), (byte[])_ciphertext.Clone());
}

/// <summary>Recommended jurisdiction-specific v2 path; low-level APIs remain available.</summary>
public static class V2Issuance
{
    /// <summary>Validate and encrypt an Australian v2 payslip using the AU2 PII profile.</summary>
    public static PreparedV2Payslip PrepareAustralian(AustralianPiiFields pii,
        AustralianPayslipV2 payslip, DateTimeOffset issuedAt, byte[] key,
        string? verifiablReference = null)
    {
        if (pii is null) { throw new ArgumentNullException(nameof(pii)); }
        if (payslip is null) { throw new ArgumentNullException(nameof(payslip)); }
        return Prepare(Pii.FormatAustralian(pii), PayslipSchemas.AustralianV2,
            PayslipNonPii.FromAustralianV2(payslip), issuedAt, key, verifiablReference);
    }

    /// <summary>Validate and encrypt a New Zealand v2 payslip using the NZ2 PII profile.</summary>
    public static PreparedV2Payslip PrepareNewZealand(NewZealandPiiFields pii,
        NewZealandPayslipV2 payslip, DateTimeOffset issuedAt, byte[] key,
        string? verifiablReference = null)
    {
        if (pii is null) { throw new ArgumentNullException(nameof(pii)); }
        if (payslip is null) { throw new ArgumentNullException(nameof(payslip)); }
        return Prepare(Pii.FormatNewZealand(pii), PayslipSchemas.NewZealandV2,
            PayslipNonPii.FromNewZealandV2(payslip), issuedAt, key, verifiablReference);
    }

    private static PreparedV2Payslip Prepare(string plaintext, string schema, PayslipNonPii payslip,
        DateTimeOffset issuedAt, byte[] key, string? reference)
    {
        string selectedReference = reference is null ? VerifiablReference.Generate() :
            VerifiablReference.Validate(reference, nameof(reference));
        // Validate the same snapshot we retain, before drawing an IV or encrypting.
        PayslipNonPii snapshot = PreparedV2Payslip.CopyPayslip(payslip);
        var placeholder = new EncryptionMetadata { Iv = new byte[12], Tag = new byte[16] };
        Wire.ToWire(new RegisterNonPiiRequest
        {
            VerifiablReference = selectedReference,
            Schema = schema,
            IssuedAt = issuedAt,
            PayslipNonPii = snapshot,
            EncryptionMetadata = placeholder,
        }, selectedReference);
        return new PreparedV2Payslip(selectedReference, schema, issuedAt, snapshot,
            VerifiablCrypto.EncryptPii(plaintext, key));
    }
}
