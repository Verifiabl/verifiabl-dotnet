namespace Verifiabl.Client;

/// <summary>
/// Error codes the API is known to return today, for matching against
/// <see cref="VerifiablApiException.Code"/>. The API may add codes over time;
/// treat anything not listed here as a generic failure rather than rejecting the
/// response.
/// </summary>
public static class VerifiablErrorCodes
{
    /// <summary>The request failed validation; see the exception's field errors.</summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>The ciphertext could not be decrypted with the registered key.</summary>
    public const string DecryptionFailed = "DECRYPTION_FAILED";

    /// <summary>The request was not authenticated.</summary>
    public const string Unauthorized = "UNAUTHORIZED";

    /// <summary>The authenticated caller may not perform this operation.</summary>
    public const string Forbidden = "FORBIDDEN";

    /// <summary>
    /// The Verifiabl reference is already registered with different data. A resend
    /// of identical content is an idempotent replay and succeeds instead.
    /// </summary>
    public const string Conflict = "CONFLICT";

    /// <summary>
    /// The record's encryption IV is already registered to this issuer. Surfaces as
    /// <see cref="VerifiablIvReuseException"/> on a single registration, and as an
    /// error result matched by <see cref="BatchRecordResult.IsIvReused"/> in a batch.
    /// </summary>
    public const string IvReused = "IV_REUSED";

    /// <summary>The referenced key version is not available for this provider.</summary>
    public const string KeyVersionUnavailable = "KEY_VERSION_UNAVAILABLE";

    /// <summary>An unexpected server error, or a non-JSON error response.</summary>
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>The service is temporarily unavailable.</summary>
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
}
