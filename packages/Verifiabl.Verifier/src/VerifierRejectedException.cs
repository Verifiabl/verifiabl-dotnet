namespace Verifiabl.Verifier;

/// <summary>
/// Thrown when the payload could not be verified. The text cannot be read,
/// Verifiabl has no record of it, its issuer has no active key, or it does not
/// decrypt to a valid payslip payload; the API does not say which. A damaged
/// scan or a code from another product or environment gives the same result.
/// Sending the same text again usually gives the same answer, so it is not retryable.
/// </summary>
/// <remarks>
/// <see cref="Code"/> is <see cref="VerifierErrorCodes.NotVerified"/>. Quote
/// <see cref="VerifierException.RequestId"/> to Verifiabl support to find the cause.
/// </remarks>
public sealed class VerifierRejectedException : VerifierException
{
    /// <summary>Create a rejection for an adapter or test fake.</summary>
    /// <remarks>Keep messages and detail free of payloads, payslip data and credentials.</remarks>
    public VerifierRejectedException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary><see cref="VerifierErrorCodes.NotVerified"/>.</summary>
    public string Code { get; }

    /// <summary>HTTP status code, or null when the client checked the text without sending it.</summary>
    public int? Status { get; init; }

    /// <summary>
    /// A finer cause, sent only by the issuer self-test routes so issuers can
    /// debug their own payslips. Verifier routes send none.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>Extra detail, when the API sends it.</summary>
    public string? Detail { get; init; }

    /// <inheritdoc />
    public override bool IsRetryable => false;
}
