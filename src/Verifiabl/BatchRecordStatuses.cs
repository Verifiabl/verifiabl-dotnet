namespace Verifiabl;

/// <summary>
/// Per-record outcome statuses the API returns today. The API may add statuses
/// over time, so treat anything not listed here as a generic outcome rather than
/// an error in your integration.
/// </summary>
public static class BatchRecordStatuses
{
    /// <summary>The record was newly registered.</summary>
    public const string Created = "created";

    /// <summary>An idempotent resend of identical content.</summary>
    public const string Duplicate = "duplicate";

    /// <summary>The record failed; see the result's Code and Detail.</summary>
    public const string Error = "error";
}
