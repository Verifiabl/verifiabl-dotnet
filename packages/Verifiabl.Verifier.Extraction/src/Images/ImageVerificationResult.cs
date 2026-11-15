namespace Verifiabl.Verifier.Extraction.Images;

/// <summary>A verified image file and the QR code read from it.</summary>
public sealed class ImageVerificationResult
{
    /// <summary>Create a result for an adapter or test fake. This does not perform verification.</summary>
    public ImageVerificationResult(VerificationResult verification, ImagePayload payload)
    {
        Verification = verification ?? throw new ArgumentNullException(nameof(verification));
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        if (payload.QrCode is null)
        {
            throw new ArgumentException("A verified image has a QR code.", nameof(payload));
        }
    }

    /// <summary>The verified payslip data, not an assessment of the image's visible contents.</summary>
    public VerificationResult Verification { get; }

    /// <summary>The QR code read from the image.</summary>
    public ImagePayload Payload { get; }

    /// <summary>The reference and schema only, so the result is safe to log.</summary>
    public override string ToString() => $"ImageVerificationResult {{ {Verification} }}";
}
