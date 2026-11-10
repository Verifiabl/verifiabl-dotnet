namespace Verifiabl.Verifier.Pdf;

/// <summary>A verified PDF and the payload found in it.</summary>
public sealed class PdfVerificationResult
{
    /// <summary>Create a result for an adapter or test fake. This does not perform verification.</summary>
    public PdfVerificationResult(VerificationResult verification, PdfPayloads payloads)
    {
        Verification = verification ?? throw new ArgumentNullException(nameof(verification));
        Payloads = payloads ?? throw new ArgumentNullException(nameof(payloads));
        if (payloads.Payload is null)
        {
            throw new ArgumentException("A verified PDF has a payload.", nameof(payloads));
        }
    }

    /// <summary>The verified payslip data, not an assessment of the PDF's visible contents.</summary>
    public VerificationResult Verification { get; }

    /// <summary>The payloads found in the PDF.</summary>
    public PdfPayloads Payloads { get; }

    /// <summary>The reference and schema only, so the result is safe to log.</summary>
    public override string ToString() => $"PdfVerificationResult {{ {Verification} }}";
}
