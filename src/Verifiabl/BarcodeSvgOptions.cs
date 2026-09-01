namespace Verifiabl;

/// <summary>QR error-correction levels used by the barcode renderer.</summary>
public enum BarcodeErrorCorrectionLevel
{
    /// <summary>~7% damage recovery. Never a ceiling; only reached by degradation.</summary>
    Low = 0,

    /// <summary>~15% damage recovery. The default ceiling.</summary>
    Medium = 1,

    /// <summary>~25% damage recovery, at the cost of a denser code.</summary>
    Quartile = 2,
}

/// <summary>Options for <see cref="VerifiablBarcode.CreateSvg"/>.</summary>
public sealed class BarcodeSvgOptions
{
    /// <summary>Printed format. Defaults to V2; select V1 only for rollback.</summary>
    public BarcodePayloadFormat Format { get; set; } = BarcodePayloadFormat.V2;

    /// <summary>API environment for the public QR scan URL. Defaults to production.</summary>
    public VerifiablEnvironment Environment { get; set; } = VerifiablEnvironment.Production;

    /// <summary>
    /// Advanced override for the public QR scan URL origin. Defaults to the
    /// selected environment's scan URL origin. Must use https.
    /// </summary>
    public Uri? ScanBaseUrl { get; set; }

    /// <summary>Total badge width in SVG user units / px (default: 480, the minimum).</summary>
    public double Width { get; set; } = 480;

    /// <summary>
    /// Highest QR error-correction level to use: <see cref="BarcodeErrorCorrectionLevel.Medium"/>
    /// (the default) or <see cref="BarcodeErrorCorrectionLevel.Quartile"/>. The
    /// renderer still steps below this only when the payload would not otherwise
    /// fit the fixed frame.
    /// </summary>
    /// <remarks>
    /// Medium (~15% damage recovery) keeps the modules large and the symbol
    /// visually clean. Choose Quartile (~25% recovery) for documents expected to
    /// take heavy print wear, accepting a denser code (one or two QR versions
    /// larger).
    /// </remarks>
    public BarcodeErrorCorrectionLevel MaxErrorCorrection { get; set; } =
        BarcodeErrorCorrectionLevel.Medium;
}

/// <summary>Result of <see cref="VerifiablBarcode.CreateSvg"/>.</summary>
public sealed class BarcodeSvgResult
{
    internal BarcodeSvgResult(
        string svg,
        double width,
        double height,
        string content,
        BarcodeErrorCorrectionLevel errorCorrectionLevel,
        int qrVersion,
        double modulePx,
        bool degraded)
    {
        Svg = svg;
        Width = width;
        Height = height;
        Content = content;
        ErrorCorrectionLevel = errorCorrectionLevel;
        QrVersion = qrVersion;
        ModulePx = modulePx;
        Degraded = degraded;
    }

    /// <summary>Complete standalone SVG document.</summary>
    public string Svg { get; }

    /// <summary>Badge width in SVG user units / px.</summary>
    public double Width { get; }

    /// <summary>Badge height in SVG user units / px.</summary>
    public double Height { get; }

    /// <summary>The exact string encoded in the QR code.</summary>
    public string Content { get; }

    /// <summary>
    /// Error-correction level actually used. Normally the configured ceiling
    /// (<see cref="BarcodeSvgOptions.MaxErrorCorrection"/>, default Medium); drops
    /// below it only for unusually long PII so the code still fits the fixed frame.
    /// </summary>
    public BarcodeErrorCorrectionLevel ErrorCorrectionLevel { get; }

    /// <summary>QR symbol version (1-40), for scanner-fixture attribution.</summary>
    public int QrVersion { get; }

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
