namespace Verifiabl.Verifier;

/// <summary>
/// Thrown when the barcode fails verification: it is not a Verifiabl payload,
/// was altered or truncated, or has no registered payslip. Treat the document as
/// unverified; retrying the same barcode gives the same answer.
/// </summary>
/// <remarks>
/// <see cref="Code"/> says why, for logs and support. Matching on it is optional.
/// </remarks>
public sealed class VerifierRejectedException : VerifierException
{
    private readonly IReadOnlyList<VerifierFieldError> _fieldErrors = Array.Empty<VerifierFieldError>();

    /// <summary>Create a rejection for an adapter or test fake.</summary>
    /// <remarks>Keep messages, detail and field errors free of payloads, payslip data and credentials.</remarks>
    public VerifierRejectedException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>
    /// Why the barcode was rejected, such as <see cref="VerifierErrorCodes.DecryptionFailed"/>.
    /// </summary>
    public string Code { get; }

    /// <summary>HTTP status code, or null when the client rejected the barcode without sending it.</summary>
    public int? Status { get; init; }

    /// <summary>A finer reason for some codes, when the API sends one.</summary>
    public string? Reason { get; init; }

    /// <summary>Extra detail, when the API sends it.</summary>
    public string? Detail { get; init; }

    /// <summary>The request fields that failed validation. Empty for other rejections.</summary>
    public IReadOnlyList<VerifierFieldError> FieldErrors
    {
        get => _fieldErrors;
        init => _fieldErrors = Array.AsReadOnly(value?.ToArray() ?? []);
    }

    /// <inheritdoc />
    public override bool IsRetryable => false;
}
