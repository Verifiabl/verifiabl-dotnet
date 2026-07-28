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
