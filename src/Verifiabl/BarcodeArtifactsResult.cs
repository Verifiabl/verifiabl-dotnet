using System.Text;

namespace Verifiabl;

/// <summary>Image representation returned by the combined artifact builder.</summary>
public enum BarcodeImageFormat
{
    /// <summary>UTF-8 SVG image bytes.</summary>
    Svg = 0,

    /// <summary>PNG image bytes.</summary>
    Png = 1,
}

/// <summary>Options for building both self-managed PDF artifacts.</summary>
public sealed class BarcodeArtifactsOptions
{
    /// <summary>Image representation for the branded barcode. Defaults to SVG.</summary>
    public BarcodeImageFormat ImageFormat { get; set; } = BarcodeImageFormat.Svg;

    /// <summary>Printed payload format. Defaults to V2; select V1 only for rollback.</summary>
    public BarcodePayloadFormat Format { get; set; } = BarcodePayloadFormat.V2;

    /// <summary>API environment for the public QR scan URL. Defaults to production.</summary>
    public VerifiablEnvironment Environment { get; set; } = VerifiablEnvironment.Production;

    /// <summary>Advanced override for the public QR scan URL origin. Must use HTTPS.</summary>
    public Uri? ScanBaseUrl { get; set; }

    /// <summary>Total SVG badge width in user units / px. Used only for SVG; defaults to 480.</summary>
    public double Width { get; set; } = 480;

    /// <summary>PNG bitmap width. Used only for PNG; defaults to 720.</summary>
    public int PixelWidth { get; set; } = 720;

    /// <summary>Highest QR error-correction level to use. Defaults to Medium.</summary>
    public BarcodeErrorCorrectionLevel MaxErrorCorrection { get; set; } =
        BarcodeErrorCorrectionLevel.Medium;

    internal BarcodeSvgOptions ToRendererOptions() => new()
    {
        Format = Format,
        Environment = Environment,
        ScanBaseUrl = ScanBaseUrl,
        Width = Width,
        MaxErrorCorrection = MaxErrorCorrection,
    };
}

/// <summary>A rendered barcode image and its QR diagnostics.</summary>
public sealed class BarcodeImageArtifact
{
    internal BarcodeImageArtifact(BarcodeSvgResult result)
    {
        Format = BarcodeImageFormat.Svg;
        Data = Encoding.UTF8.GetBytes(result.Svg);
        Width = result.Width;
        Height = result.Height;
        Content = result.Content;
        ErrorCorrectionLevel = result.ErrorCorrectionLevel;
        QrVersion = result.QrVersion;
        ModulePx = result.ModulePx;
        Degraded = result.Degraded;
    }

    internal BarcodeImageArtifact(BarcodePngResult result)
    {
        Format = BarcodeImageFormat.Png;
        Data = result.Png;
        Width = result.Width;
        Height = result.Height;
        Content = result.Content;
        ErrorCorrectionLevel = result.ErrorCorrectionLevel;
        QrVersion = result.QrVersion;
        ModulePx = result.ModulePx;
        Degraded = result.Degraded;
    }

    /// <summary>Image representation stored in <see cref="Data"/>.</summary>
    public BarcodeImageFormat Format { get; }

    /// <summary>UTF-8 SVG bytes or PNG bytes according to <see cref="Format"/>.</summary>
    public byte[] Data { get; }

    /// <summary>Rendered width in SVG units or bitmap pixels.</summary>
    public double Width { get; }

    /// <summary>Rendered height in SVG units or bitmap pixels.</summary>
    public double Height { get; }

    /// <summary>The exact string encoded in the QR code.</summary>
    public string Content { get; }

    /// <summary>Error-correction level actually used.</summary>
    public BarcodeErrorCorrectionLevel ErrorCorrectionLevel { get; }

    /// <summary>QR symbol version (1-40).</summary>
    public int QrVersion { get; }

    /// <summary>Rendered size of one QR module.</summary>
    public double ModulePx { get; }

    /// <summary>Whether rendering traded scan robustness to fit the payload.</summary>
    public bool Degraded { get; }
}

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
    internal BarcodeArtifactsResult(BarcodeImageArtifact barcode, BarcodePdfMetadata pdfMetadata)
    {
        Barcode = barcode;
        PdfMetadata = pdfMetadata;
    }

    /// <summary>The branded SVG or PNG barcode to embed in the PDF.</summary>
    public BarcodeImageArtifact Barcode { get; }

    /// <summary>The matching XMP metadata copy to write into the PDF.</summary>
    public BarcodePdfMetadata PdfMetadata { get; }
}
