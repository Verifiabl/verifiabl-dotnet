namespace Verifiabl;

/// <summary>
/// Per-record outcome of a batch registration, index-aligned to the submitted
/// records. <see cref="Code"/> and <see cref="Detail"/> accompany an "error"
/// status. One bad record never fails the whole batch.
/// </summary>
public sealed class BatchRecordResult
{
    internal BatchRecordResult(
        int index,
        string status,
        string verifiablReference,
        string? code,
        string? detail)
    {
        Index = index;
        Status = status;
        VerifiablReference = verifiablReference;
        Code = code;
        Detail = detail;
    }

    /// <summary>Position of this record in the submitted batch.</summary>
    public int Index { get; }

    /// <summary>
    /// Per-record status. Compare against <see cref="BatchRecordStatuses"/>; the
    /// API may add statuses over time, so an unknown status flows through rather
    /// than failing the whole response.
    /// </summary>
    public string Status { get; }

    /// <summary>The Verifiabl reference submitted with this record.</summary>
    public string VerifiablReference { get; }

    /// <summary>Machine-readable error code, present when <see cref="Status"/> is "error".</summary>
    public string? Code { get; }

    /// <summary>Human-readable error detail, present when <see cref="Status"/> is "error".</summary>
    public string? Detail { get; }
}

/// <summary>Response from <see cref="VerifiablClient.RegisterNonPiiBatchAsync"/>.</summary>
public sealed class RegisterNonPiiBatchResponse
{
    internal RegisterNonPiiBatchResponse(IReadOnlyList<BatchRecordResult> results)
    {
        Results = results;
    }

    /// <summary>Per-record outcomes, index-aligned to the submitted records.</summary>
    public IReadOnlyList<BatchRecordResult> Results { get; }
}
