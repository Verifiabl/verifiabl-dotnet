namespace Verifiabl;

/// <summary>Result of <see cref="VerifiablBarcode.CreatePng"/>.</summary>
public sealed class BarcodePngResult
{
    internal BarcodePngResult(
        byte[] png,
        int width,
        int height,
        string content,
        BarcodeErrorCorrectionLevel errorCorrectionLevel,
        double modulePx,
        bool degraded)
    {
        Png = png;
        Width = width;
        Height = height;
        Content = content;
        ErrorCorrectionLevel = errorCorrectionLevel;
        ModulePx = modulePx;
        Degraded = degraded;
    }

    /// <summary>PNG image bytes.</summary>
    public byte[] Png { get; }

    /// <summary>Bitmap width in pixels.</summary>
    public int Width { get; }

    /// <summary>Bitmap height in pixels.</summary>
    public int Height { get; }

    /// <summary>The exact string encoded in the QR code.</summary>
    public string Content { get; }

    /// <summary>
    /// Error-correction level actually used. Normally the configured ceiling
    /// (<see cref="BarcodeSvgOptions.MaxErrorCorrection"/>, default Medium); drops
    /// below it only for unusually long PII so the code still fits the fixed frame.
    /// </summary>
    public BarcodeErrorCorrectionLevel ErrorCorrectionLevel { get; }

    /// <summary>Rendered size of one QR module, in output pixels.</summary>
    public double ModulePx { get; }

    /// <summary>
    /// True when the renderer had to trade scan robustness to fit the payload
    /// (error correction below the configured ceiling, or modules below the ideal
    /// size). False for essentially all real records. Log this to observe the
    /// long tail at scale.
    /// </summary>
    public bool Degraded { get; }
}
