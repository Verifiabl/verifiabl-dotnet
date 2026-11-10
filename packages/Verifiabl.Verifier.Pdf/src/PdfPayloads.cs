using Verifiabl.Verifier.Extraction;

namespace Verifiabl.Verifier.Pdf;

/// <summary>The Verifiabl payload found in a PDF.</summary>
/// <remarks>
/// <para>
/// <see cref="PayslipPdfReader"/> reads the metadata payload first and reads the
/// PDF's images for a QR code only when there is none, so it sets at most one of
/// <see cref="Metadata"/> and <see cref="QrCode"/>.
/// </para>
/// <para>The payload text is encrypted, but treat it as sensitive and keep it out of logs.</para>
/// </remarks>
public sealed class PdfPayloads
{
    /// <summary>Create an instance for an adapter or test fake. This reads nothing.</summary>
    public PdfPayloads(string? metadata, string? qrCode, int skippedImages = 0)
        : this(metadata, qrCode, skippedImages, opened: true)
    {
        if (skippedImages < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(skippedImages));
        }
    }

    internal PdfPayloads(string? metadata, string? qrCode, int skippedImages, bool opened)
    {
        Metadata = metadata;
        QrCode = qrCode;
        SkippedImages = skippedImages;
        Opened = opened;
    }

    /// <summary>The payload in the PDF's XMP metadata, or null.</summary>
    public string? Metadata { get; }

    /// <summary>
    /// The text of the Verifiabl QR code in the PDF's images, or null. The reader
    /// leaves it null when <see cref="Metadata"/> was found.
    /// </summary>
    public string? QrCode { get; }

    /// <summary>
    /// Embedded images or pages that could not be read, such as JPEG images when
    /// <see cref="PdfReadOptions.JpegDecoder"/> is not set. One of them may hold the QR code.
    /// </summary>
    public int SkippedImages { get; }

    /// <summary>The metadata payload, or the QR code when there is none, or null.</summary>
    internal string? Payload => Metadata ?? QrCode;

    /// <summary>False when the PDF's structure could not be parsed, so no images were read.</summary>
    internal bool Opened { get; }

    /// <summary>Which payloads were found and the skipped-image count only, so the object is safe to log.</summary>
    public override string ToString() =>
        $"PdfPayloads {{ HasMetadata = {Metadata is not null}, HasQrCode = {QrCode is not null}, SkippedImages = {SkippedImages} }}";
}
