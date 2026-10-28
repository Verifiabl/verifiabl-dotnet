using System.Text.Json;

namespace Verifiabl.Verifier;

/// <summary>Issuer-authenticated data returned for a verified payload, not an assessment of a document.</summary>
/// <remarks>
/// <see cref="Employee"/> holds personal information. Keep it out of logs.
/// <see cref="ToString"/> omits it and the payslip values.
/// </remarks>
public sealed class VerificationResult
{
    /// <summary>Create a result for an adapter or test fake. This does not perform verification.</summary>
    /// <remarks>
    /// JSON objects are cloned so they outlive their source documents. Unknown
    /// schemas and fields are preserved; no lender-specific mapping is applied.
    /// </remarks>
    public VerificationResult(
        string verifiablReference,
        string? schema,
        JsonElement payslip,
        JsonElement employee,
        DateTimeOffset decryptedAt)
    {
        if (string.IsNullOrWhiteSpace(verifiablReference))
        {
            throw new ArgumentException("A reference is required.", nameof(verifiablReference));
        }

        if (payslip.ValueKind != JsonValueKind.Object || employee.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Payslip and employee must be JSON objects.");
        }

        VerifiablReference = verifiablReference;
        Schema = schema;
        Payslip = payslip.Clone();
        Employee = employee.Clone();
        DecryptedAt = decryptedAt;
    }

    /// <summary>The payslip's Verifiabl reference.</summary>
    public string VerifiablReference { get; }

    /// <summary>The payslip schema, such as <c>au.payslip.v2</c>, when the API sends one.</summary>
    public string? Schema { get; }

    /// <summary>
    /// The registered payslip record: <c>schema</c>, <c>issued_at</c> and
    /// <c>payslip_non_pii</c>, as the API returns them. Money values are decimal strings.
    /// </summary>
    public JsonElement Payslip { get; }

    /// <summary>The employee details decrypted from the barcode, as the API returns them.</summary>
    public JsonElement Employee { get; }

    /// <summary>When Verifiabl decrypted the barcode.</summary>
    public DateTimeOffset DecryptedAt { get; }

    /// <summary>The reference and schema only, so the result is safe to log.</summary>
    public override string ToString() =>
        $"VerificationResult {{ VerifiablReference = {VerifiablReference}, Schema = {Schema ?? "unknown"} }}";
}
