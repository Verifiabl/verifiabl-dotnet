namespace Verifiabl.Verifier.Extraction.Pdf;

/// <summary>The Verifiabl payload found in a PDF.</summary>
/// <remarks>
/// <para>
/// <see cref="PayslipPdfReader"/> reads the metadata payload first and renders the
/// PDF's pages for a QR code only when there is none, so it sets at most one of
/// <see cref="Metadata"/> and <see cref="QrCode"/>.
/// </para>
/// <para>The payload text is encrypted, but treat it as sensitive and keep it out of logs.</para>
/// </remarks>
public sealed class PdfPayloads
{
    /// <summary>Create an instance for an adapter or test fake. This reads nothing.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="renderedPages"/> is negative.</exception>
    public PdfPayloads(string? metadata, string? qrCode, int renderedPages = 0)
    {
        if (renderedPages < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(renderedPages));
        }

        Metadata = metadata;
        QrCode = qrCode;
        RenderedPages = renderedPages;
    }

    /// <summary>The payload in the PDF's XMP metadata, or null.</summary>
    public string? Metadata { get; }

    /// <summary>
    /// The text of the Verifiabl QR code on the PDF's pages, or null. The reader
    /// leaves it null when <see cref="Metadata"/> was found.
    /// </summary>
    public string? QrCode { get; }

    /// <summary>How many pages were rendered to look for a QR code: 0 when the PDF has a metadata payload.</summary>
    public int RenderedPages { get; }

    /// <summary>The metadata payload, or the QR code when there is none, or null.</summary>
    internal string? Payload => Metadata ?? QrCode;

    /// <summary>Which payloads were found and the page count only, so the object is safe to log.</summary>
    public override string ToString() =>
        $"PdfPayloads {{ HasMetadata = {Metadata is not null}, HasQrCode = {QrCode is not null}, RenderedPages = {RenderedPages} }}";
}
