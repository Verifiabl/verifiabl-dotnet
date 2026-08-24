namespace Verifiabl.Client;

/// <summary>
/// Per-record outcome of a batch registration, in the same order as the
/// submitted records (so <c>Results[i]</c> is the outcome of record <c>i</c>).
/// <see cref="Code"/> and <see cref="Detail"/> accompany an "error" status.
/// One bad record never fails the whole batch. Correlate by position, by the
/// record's <see cref="ExternalId"/>, or by <see cref="VerifiablReference"/>.
/// </summary>
public sealed class BatchRecordResult
{
    internal BatchRecordResult(
        string status,
        string verifiablReference,
        string? externalId,
        string? code,
        string? detail)
    {
        Status = status;
        VerifiablReference = verifiablReference;
        ExternalId = externalId;
        Code = code;
        Detail = detail;
    }

    /// <summary>
    /// Per-record status. Compare against <see cref="BatchRecordStatuses"/>; the
    /// API may add statuses over time, so an unknown status flows through rather
    /// than failing the whole response.
    /// </summary>
    public string Status { get; }

    /// <summary>The Verifiabl reference submitted with this record.</summary>
    public string VerifiablReference { get; }

    /// <summary>The record's caller-supplied external id, echoed back when one was supplied.</summary>
    public string? ExternalId { get; }

    /// <summary>Machine-readable error code, present when <see cref="Status"/> is "error".</summary>
    public string? Code { get; }

    /// <summary>Human-readable error detail, present when <see cref="Status"/> is "error".</summary>
    public string? Detail { get; }

    /// <summary>
    /// True when the API rejected this record because its encryption IV is already
    /// registered to your issuer, either against a stored record or against an
    /// earlier record in the same batch (that first record still registers).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two cases differ only in <see cref="Detail"/>, so match on this rather
    /// than on the wording.
    /// </para>
    /// <para>
    /// AES-256-GCM requires a unique IV for every record encrypted under one key,
    /// so this reports a fault in the calling integration rather than a transient
    /// failure. The SDK deliberately does not re-encrypt and resubmit the record
    /// for you: <see cref="VerifiablCrypto.EncryptPii"/> already draws a fresh IV
    /// on every call, so a collision means encryption metadata was replayed, and
    /// papering over it would hide a broken integration. Encrypt the payslip again,
    /// resend the record with the new encryption metadata, and rebuild any barcode
    /// already rendered from the previous ciphertext.
    /// </para>
    /// </remarks>
    public bool IsIvReused =>
        Status == BatchRecordStatuses.Error && Code == VerifiablErrorCodes.IvReused;
}

/// <summary>Response from <see cref="IVerifiablClient.RegisterNonPiiBatchAsync"/>.</summary>
public sealed class RegisterNonPiiBatchResponse
{
    internal RegisterNonPiiBatchResponse(IReadOnlyList<BatchRecordResult> results)
    {
        Results = results;
    }

    /// <summary>Per-record outcomes, in the same order as the submitted records.</summary>
    public IReadOnlyList<BatchRecordResult> Results { get; }
}
