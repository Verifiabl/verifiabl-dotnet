namespace Verifiabl.Verifier;

/// <summary>
/// Thrown by optional extraction packages, such as <c>Verifiabl.Verifier.Pdf</c>,
/// when no single Verifiabl payload could be read from a document or image.
/// Nothing was sent to Verifiabl.
/// </summary>
/// <remarks>
/// No payload was verified, and this is not a failed verification. The input
/// may not carry a Verifiabl payload, or the copy may be too poor to read;
/// <see cref="Failure"/> says which.
/// Messages never contain payload text.
/// </remarks>
public sealed class PayloadExtractionException : VerifierException
{
    /// <summary>Create an instance for an extraction package, adapter or test fake.</summary>
    /// <remarks>Keep the message and cause free of payloads, images and personal data.</remarks>
    public PayloadExtractionException(PayloadExtractionFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    /// <summary>Why extraction failed.</summary>
    public PayloadExtractionFailure Failure { get; }

    /// <summary>
    /// True only for <see cref="PayloadExtractionFailure.TimedOut"/>, which may
    /// succeed with less load. Other failures repeat for the same input.
    /// </summary>
    public override bool IsRetryable => Failure == PayloadExtractionFailure.TimedOut;
}

/// <summary>Why a <see cref="PayloadExtractionException"/> was thrown.</summary>
/// <remarks>Later versions may add values; treat unknown values like <see cref="Unreadable"/>.</remarks>
public enum PayloadExtractionFailure
{
    /// <summary>The input holds no readable Verifiabl payload.</summary>
    NotFound = 0,

    /// <summary>
    /// The input holds two different Verifiabl payloads in the same kind of copy,
    /// such as two QR codes. Verify each payslip separately.
    /// </summary>
    Ambiguous = 1,

    /// <summary>The input is larger or more complex than a payslip needs, so it was not read in full.</summary>
    LimitExceeded = 2,

    /// <summary>The input could not be parsed at all, such as a damaged or encrypted PDF.</summary>
    Unreadable = 3,

    /// <summary>Extraction did not finish within its time budget.</summary>
    TimedOut = 4,
}
