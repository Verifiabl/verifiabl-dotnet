namespace Verifiabl.Verifier.Extraction.Images;

/// <summary>The Verifiabl QR code found in an image file.</summary>
/// <remarks>The payload text is encrypted, but treat it as sensitive and keep it out of logs.</remarks>
public sealed class ImagePayload
{
    /// <summary>Create an instance for an adapter or test fake. This reads nothing.</summary>
    public ImagePayload(string? qrCode)
    {
        QrCode = qrCode;
    }

    /// <summary>The text of the image's Verifiabl QR code, unchanged, or null when it has none.</summary>
    public string? QrCode { get; }

    /// <summary>Whether a QR code was found only, so the object is safe to log.</summary>
    public override string ToString() => $"ImagePayload {{ HasQrCode = {QrCode is not null} }}";
}
