namespace Verifiabl.Verifier;

/// <summary>
/// Error codes in <see cref="VerifierRejectedException.Code"/>,
/// <see cref="VerifierAuthException.Code"/> and <see cref="VerifierServiceException.Code"/>.
/// </summary>
public static class VerifierErrorCodes
{
    /// <summary>
    /// The barcode is not a recognised Verifiabl payload or scan link. Thrown as
    /// <see cref="VerifierRejectedException"/>.
    /// </summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>
    /// The ciphertext did not authenticate. The barcode was altered, truncated or
    /// not issued by Verifiabl. Thrown as <see cref="VerifierRejectedException"/>.
    /// </summary>
    public const string DecryptionFailed = "DECRYPTION_FAILED";

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
    /// No payslip is registered under the barcode's reference. Thrown as
    /// <see cref="VerifierRejectedException"/>.
    /// </summary>
    public const string VerifiablReferenceNotFound = "VERIFIABL_REFERENCE_NOT_FOUND";

    /// <summary>
    /// The request body is larger than the API accepts. The body holds only the
    /// barcode, so this is thrown as <see cref="VerifierRejectedException"/>.
    /// </summary>
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";

    /// <summary>The request content type is not supported.</summary>
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";

    /// <summary>
    /// The issuer's key for this payslip is not available. Not a rejection and not
    /// retryable; contact Verifiabl support.
    /// </summary>
    public const string KeyVersionUnavailable = "KEY_VERSION_UNAVAILABLE";

    /// <summary>Too many requests. Retried automatically up to <see cref="VerifierClientOptions.MaxRetries"/>.</summary>
    public const string RateLimited = "RATE_LIMITED";

    /// <summary>An unexpected server error, or a response with no Verifiabl error body.</summary>
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>A Verifiabl dependency is unavailable. Retried automatically.</summary>
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";

    private static readonly string[] RejectionCodes =
    [
        ValidationFailed,
        DecryptionFailed,
        VerifiablReferenceNotFound,
        PayloadTooLarge,
    ];

    /// <summary>Whether the code means the barcode itself failed, so a retry gives the same answer.</summary>
    internal static bool IsRejection(string code) => Array.IndexOf(RejectionCodes, code) >= 0;
}
