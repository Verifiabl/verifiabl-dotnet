namespace Verifiabl.Internal;

/// <summary>
/// Branded "Secured by Verifiabl" PNG renderer: a deterministic composite of
/// the pre-rasterised frame and the exact-geometry QR content. Mirrors the
/// Node SDK's PNG pipeline; the two SDKs produce byte-identical rasters for
/// the same record.
/// </summary>
internal static class PngBadgeRenderer
{
    internal static BarcodePngResult Render(BarcodeParts parts, BarcodeSvgOptions options, int pixelWidth)
    {
        CompositedBadge badge = Compose(parts, options, pixelWidth);
        byte[] png = PngEncoder.Encode(badge.Rgba, badge.Width, badge.Height);
        return new BarcodePngResult(
            png,
            badge.Width,
            badge.Height,
            badge.Content,
            badge.ErrorCorrectionLevel,
            badge.QrVersion,
            badge.ModulePx,
            badge.Degraded);
    }

    /// <summary>The raster before PNG encoding; the cross-SDK parity tests compare this.</summary>
    internal static CompositedBadge Compose(BarcodeParts parts, BarcodeSvgOptions options, int pixelWidth)
    {
        SvgBadgeRenderer.BadgeGeometry geometry = SvgBadgeRenderer.GeometryFor(options.Layout);
        if (pixelWidth == 0)
        {
            pixelWidth = DefaultPixelWidth(options.Layout);
        }

        if (!FrameAssets.IsSupported(options.Layout, pixelWidth))
        {
            throw new ArgumentException(
                $"pixelWidth must be one of {string.Join(", ", FrameAssets.SupportedWidths(options.Layout))} "
                + $"for the {options.Layout.ToString().ToLowerInvariant()} layout.",
                nameof(pixelWidth));
        }

        var scanOptions = new ScanUrlOptions
        {
            Environment = options.Environment,
            ScanBaseUrl = options.ScanBaseUrl,
        };
        string content = VerifiablBarcode.BuildScanUrl(parts, scanOptions);

        BarcodeErrorCorrectionLevel[] ladder =
            SvgBadgeRenderer.ErrorCorrectionLadder(options.MaxErrorCorrection);
        SvgBadgeRenderer.SelectedQrRendering selected =
            SvgBadgeRenderer.SelectQrRendering(content, pixelWidth, ladder, geometry);
        bool degraded = selected.ErrorCorrectionLevel != ladder[0]
            || selected.ModulePx < SvgBadgeRenderer.IdealModulePx;

        FrameAssets.ParsedFrame frame = FrameAssets.Load(options.Layout, pixelWidth);
        byte[] rgba = FrameAssets.ExpandRgba(frame);
        BadgeCompositor.BlitQrOntoFrame(
            rgba,
            frame.Width,
            selected.Qr,
            selected.Size,
            pixelWidth,
            geometry);

        return new CompositedBadge(
            rgba,
            frame.Width,
            frame.Height,
            content,
            selected.ErrorCorrectionLevel,
            selected.QrVersion,
            SvgBadgeRenderer.Round2(selected.ModulePx),
            degraded);
    }

    /// <summary>
    /// The width that renders the QR code at the vertical default's size: 720
    /// for the vertical layout and 1410 for the horizontal layout.
    /// </summary>
    internal static int DefaultPixelWidth(BarcodeLayout layout) =>
        layout == BarcodeLayout.Horizontal ? 1410 : 720;

    internal readonly struct CompositedBadge
    {
        internal CompositedBadge(
            byte[] rgba,
            int width,
            int height,
            string content,
            BarcodeErrorCorrectionLevel errorCorrectionLevel,
            int qrVersion,
            double modulePx,
            bool degraded)
        {
            Rgba = rgba;
            Width = width;
            Height = height;
            Content = content;
            ErrorCorrectionLevel = errorCorrectionLevel;
            QrVersion = qrVersion;
            ModulePx = modulePx;
            Degraded = degraded;
        }

        internal byte[] Rgba { get; }

        internal int Width { get; }

        internal int Height { get; }

        internal string Content { get; }

        internal BarcodeErrorCorrectionLevel ErrorCorrectionLevel { get; }

        internal int QrVersion { get; }

        internal double ModulePx { get; }

        internal bool Degraded { get; }
    }
}
