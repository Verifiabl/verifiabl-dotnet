using Verifiabl.Verifier.Internal;

namespace Verifiabl.Verifier;

/// <summary>
/// Thrown when Verifiabl returns an error that is not about the barcode or the
/// credentials, such as an outage, a rate limit or an unavailable key. Check
/// <see cref="VerifierException.IsRetryable"/>, and quote
/// <see cref="VerifierException.RequestId"/> to Verifiabl support. Match on
/// <see cref="Code"/>, not the message.
/// </summary>
/// <remarks>
/// Rejected barcodes throw <see cref="VerifierRejectedException"/>, and 401 or 403
/// responses throw <see cref="VerifierAuthException"/>.
/// </remarks>
public sealed class VerifierServiceException : VerifierException
{
    private readonly IReadOnlyList<VerifierFieldError> _fieldErrors = Array.Empty<VerifierFieldError>();

    /// <summary>Create a service failure for an adapter or test fake.</summary>
    /// <remarks>Keep messages, detail and field errors free of payloads, payslip data and credentials.</remarks>
    public VerifierServiceException(int status, string code, string message)
        : base(message)
    {
        Status = status;
        Code = code;
    }

    /// <summary>HTTP status code.</summary>
    public int Status { get; }

    /// <summary>
    /// Stable error code, such as <see cref="VerifierErrorCodes.KeyVersionUnavailable"/>.
    /// <see cref="VerifierErrorCodes.InternalError"/> when the response had no
    /// Verifiabl error body.
    /// </summary>
    public string Code { get; }

    /// <summary>A finer reason for some codes, when the API sends one.</summary>
    public string? Reason { get; init; }

    /// <summary>Extra detail, when the API sends it.</summary>
    public string? Detail { get; init; }

    /// <summary>The request fields that failed validation. Empty for other errors.</summary>
    public IReadOnlyList<VerifierFieldError> FieldErrors
    {
        get => _fieldErrors;
        init => _fieldErrors = Array.AsReadOnly(value?.ToArray() ?? []);
    }

    /// <summary>True for 408, 429 and 5xx responses.</summary>
    public override bool IsRetryable => ApiTransport.IsRetryableStatus(Status);

    internal static VerifierServiceException FromResponse(
        int status,
        VerifierErrorBody? body,
        string? requestId,
        TimeSpan? retryAfter)
    {
        return new VerifierServiceException(
            status,
            body?.Code ?? VerifierErrorCodes.InternalError,
            body?.Error ?? $"Verifiabl verification request failed with status {status}.")
        {
            RequestId = requestId,
            RetryAfter = retryAfter,
            Reason = body?.Reason,
            Detail = body?.Detail,
            FieldErrors = body?.FieldErrors ?? [],
        };
    }
}
