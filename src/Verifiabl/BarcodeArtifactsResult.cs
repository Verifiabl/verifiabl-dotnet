namespace Verifiabl;

/// <summary>XMP metadata copy to write alongside the rendered QR barcode.</summary>
public sealed class BarcodePdfMetadata
{
    internal BarcodePdfMetadata(string payload)
    {
        Payload = payload;
    }

    /// <summary>XMP namespace URI for the Verifiabl payload property.</summary>
    public string XmpNamespace => VerifiablBarcode.PdfPayloadXmpNamespace;

    /// <summary>Local XMP property name; with the verifiabl prefix this is verifiabl:payload.</summary>
    public string XmpProperty => VerifiablBarcode.PdfPayloadXmpProperty;

    /// <summary>Pipe-delimited encrypted payload to write into XMP metadata.</summary>
    public string Payload { get; }
}

/// <summary>Matching local artifacts for one self-managed payslip PDF.</summary>
public sealed class BarcodeArtifactsResult
{
    internal BarcodeArtifactsResult(BarcodeSvgResult barcode, BarcodePdfMetadata pdfMetadata)
    {
        Barcode = barcode;
        PdfMetadata = pdfMetadata;
    }

    /// <summary>The branded SVG barcode to embed in the PDF.</summary>
    public BarcodeSvgResult Barcode { get; }

    /// <summary>The matching XMP metadata copy to write into the PDF.</summary>
    public BarcodePdfMetadata PdfMetadata { get; }
}
