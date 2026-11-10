namespace Verifiabl.Verifier;

/// <summary>
/// Error codes in <see cref="VerifierRejectedException.Code"/>,
/// <see cref="VerifierAuthException.Code"/> and <see cref="VerifierServiceException.Code"/>.
/// </summary>
public static class VerifierErrorCodes
{
    /// <summary>
    /// The payload could not be verified: the barcode text is unreadable, fails
    /// decryption, or has no registered payslip. The API does not say which.
    /// Thrown as <see cref="VerifierRejectedException"/>.
    /// </summary>
    public const string NotVerified = "NOT_VERIFIED";

    /// <summary>
    /// The request was malformed, so the SDK and API disagree about its format.
    /// Thrown as a non-retryable <see cref="VerifierServiceException"/> with
    /// <see cref="VerifierServiceException.FieldErrors"/>.
    /// </summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>
    /// The access token was missing or invalid, even after a refresh. Thrown as
    /// <see cref="VerifierAuthException"/>.
    /// </summary>
    public const string Unauthorized = "UNAUTHORIZED";

    /// <summary>
    /// The credentials cannot verify payslips. Thrown as
    /// <see cref="VerifierAuthException"/>.
    /// </summary>
    public const string Forbidden = "FORBIDDEN";

    /// <summary>
    /// The request body is larger than the API accepts. The client limits the
    /// barcode length first, so this is a non-retryable
    /// <see cref="VerifierServiceException"/>.
    /// </summary>
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";

    /// <summary>
    /// The request content type is not supported. Thrown as a non-retryable
    /// <see cref="VerifierServiceException"/>.
    /// </summary>
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";

    /// <summary>
    /// The <see cref="VerificationRequest.IdempotencyKey"/> was used for a
    /// different payslip, or first used more than 1 hour ago. Nothing was
    /// verified or charged. Thrown as a non-retryable
    /// <see cref="VerifierServiceException"/>. To verify again, use a new
    /// <see cref="VerificationRequest"/>.
    /// </summary>
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";

    /// <summary>Too many requests. Retried automatically up to <see cref="VerifierClientOptions.MaxRetries"/>.</summary>
    public const string RateLimited = "RATE_LIMITED";

    /// <summary>An unexpected server error, or a response with no Verifiabl error body. Retried automatically.</summary>
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>A Verifiabl dependency is unavailable. Retried automatically.</summary>
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";

    /// <summary>Whether the code means the payload could not be verified, so a retry gives the same answer.</summary>
    internal static bool IsRejection(string code) => code == NotVerified;
}
