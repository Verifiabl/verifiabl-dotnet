namespace Verifiabl.Verifier.Extraction.Internal;

/// <summary>What the module found in a PDF, unfiltered.</summary>
internal sealed class SandboxedPdfPayloads
{
    internal SandboxedPdfPayloads(IReadOnlyList<string> metadataPayloads, IReadOnlyList<string> qrCodes, int pages)
    {
        MetadataPayloads = metadataPayloads;
        QrCodes = qrCodes;
        Pages = pages;
    }

    /// <summary>
    /// Every <c>payload</c> property in the PDF's XMP metadata streams, trimmed, of at most
    /// <see cref="VerifierClient.MaxBarcodeLength"/> characters.
    /// </summary>
    internal IReadOnlyList<string> MetadataPayloads { get; }

    /// <summary>Every QR code's text on the rendered pages; empty when the PDF has a metadata payload, which is never rendered.</summary>
    internal IReadOnlyList<string> QrCodes { get; }

    /// <summary>How many pages were rendered.</summary>
    internal int Pages { get; }

    /// <summary>Describes the counts without payload text.</summary>
    public override string ToString() =>
        $"SandboxedPdfPayloads {{ MetadataPayloads = {MetadataPayloads.Count}, QrCodes = {QrCodes.Count}, Pages = {Pages} }}";
}
