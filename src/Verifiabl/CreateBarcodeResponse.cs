namespace Verifiabl;

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

/// <summary>Response from <see cref="VerifiablClient.CreateBarcodeAsync"/>.</summary>
public sealed class CreateBarcodeResponse
{
    internal CreateBarcodeResponse(string verifiablReference, BarcodeImage barcode)
    {
        VerifiablReference = verifiablReference;
        Barcode = barcode;
    }

    /// <summary>22-character base64url Verifiabl reference embedded in the returned barcode.</summary>
    public string VerifiablReference { get; }

    /// <summary>The server-generated barcode image.</summary>
    public BarcodeImage Barcode { get; }
}
