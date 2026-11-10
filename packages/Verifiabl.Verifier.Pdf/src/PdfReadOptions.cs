using Verifiabl.Verifier.Extraction;

namespace Verifiabl.Verifier.Pdf;

/// <summary>Options for reading the payload in a PDF.</summary>
public sealed class PdfReadOptions
{
    /// <summary>
    /// Time budget for reading a PDF, including reading a stream. Defaults to 30
    /// seconds. It is separate from <see cref="VerifierClientOptions.Timeout"/>,
    /// which covers the verification call that follows.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Decodes JPEG images, which scanners and phone scanning apps usually
    /// produce. This package has no JPEG decoder of its own, so without one
    /// JPEG images are skipped and counted in <see cref="PdfPayloads.SkippedImages"/>.
    /// </summary>
    /// <remarks>
    /// It receives a JPEG file's bytes, after its header was checked against the
    /// size the PDF declares. Throw <see cref="ArgumentException"/> for a JPEG it
    /// cannot read; that image is then skipped. Other exceptions propagate.
    /// </remarks>
    public Func<byte[], QrImage>? JpegDecoder { get; set; }
}
