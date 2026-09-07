namespace Verifiabl.Client;

/// <summary>A barcode image returned by the API.</summary>
public sealed class BarcodeImage
{
    internal BarcodeImage(string format, string data)
    {
        Format = format;
        Data = data;
    }

    /// <summary>Image format. Currently always "png".</summary>
    public string Format { get; }

    /// <summary>Base64-encoded image bytes.</summary>
    public string Data { get; }
}

/// <summary>PDF XMP metadata copy returned by the API.</summary>
public sealed class PdfMetadata
{
    internal PdfMetadata(string xmpNamespace, string xmpProperty, string payload)
    {
        XmpNamespace = xmpNamespace;
        XmpProperty = xmpProperty;
        Payload = payload;
    }

    /// <summary>XMP namespace URI for the Verifiabl payload property.</summary>
    public string XmpNamespace { get; }

    /// <summary>Local XMP property name; with the verifiabl prefix this appears as verifiabl:payload.</summary>
    public string XmpProperty { get; }

    /// <summary>Pipe-delimited payload to write into XMP metadata.</summary>
    public string Payload { get; }
}

/// <summary>Response from <see cref="IVerifiablClient.RegisterAndBuildBarcodeAsync"/>.</summary>
public sealed class RegisterAndBuildBarcodeResponse
{
    internal RegisterAndBuildBarcodeResponse(string verifiablReference, BarcodeImage barcode)
    {
        VerifiablReference = verifiablReference;
        Barcode = barcode;
    }

    /// <summary>22-character base64url Verifiabl reference embedded in the returned barcode.</summary>
    public string VerifiablReference { get; }

    /// <summary>The server-generated barcode image.</summary>
    public BarcodeImage Barcode { get; }
}

/// <summary>Response from <see cref="IVerifiablClient.RegisterAndBuildBarcodeArtifactsAsync"/>.</summary>
public sealed class RegisterAndBuildBarcodeArtifactsResponse
{
    internal RegisterAndBuildBarcodeArtifactsResponse(
        string verifiablReference,
        BarcodeImage barcode,
        PdfMetadata pdfMetadata)
    {
        VerifiablReference = verifiablReference;
        Barcode = barcode;
        PdfMetadata = pdfMetadata;
    }

    /// <summary>22-character base64url Verifiabl reference embedded in the returned barcode.</summary>
    public string VerifiablReference { get; }

    /// <summary>The server-generated barcode image.</summary>
    public BarcodeImage Barcode { get; }

    /// <summary>The PDF XMP metadata copy to write alongside the QR barcode.</summary>
    public PdfMetadata PdfMetadata { get; }
}
