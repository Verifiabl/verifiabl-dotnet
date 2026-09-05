using Net.Codecrete.QrCodeGenerator;

namespace Verifiabl.Internal;

/// <summary>
/// Deterministic QR compositor: draws the payload-dependent QR content (data
/// modules and rounded finder patterns) onto a pre-rasterised frame.
/// </summary>
/// <remarks>
/// Everything here is integer arithmetic on exact rational coordinates. Module
/// geometry is rational by construction (modulePx = BW / (96(n + 2i)) for the
/// QR box size B), so the same inputs produce the identical raster in every
/// implementation of this spec; this file mirrors blit.ts in the Node SDK, and
/// CI byte-compares the rasters. Do not introduce floating point here:
/// cross-runtime float differences (e.g. x87 on .NET Framework x86) would
/// silently break parity.
/// </remarks>
internal static class BadgeCompositor
{
    /// <summary>Subsamples per axis for finder anti-aliasing coverage.</summary>
    private const int Subsamples = 8;
    private const int SubsampleCount = Subsamples * Subsamples;
    private const int SubTwice = 2 * Subsamples;

    /// <summary>Finder corner radii in eightieths of a module: 1.4m, 1.0m, 0.65m.</summary>
    private const int RadiusDenom = 80;
    private const int OuterRadius80ths = 112;
    private const int InnerRadius80ths = 80;
    private const int DotRadius80ths = 52;

    // Q units per pixel-over-denom: lcm(SubTwice, RadiusDenom), so subsample
    // centres and the corner radii are both integral in Q for every box size and width.
    private const int QPerPixel = 80;
    private const int QPerSubsampleStep = QPerPixel / SubTwice;

    /// <summary>Draw the QR modules and finders onto <paramref name="rgba"/> in place.</summary>
    internal static void BlitQrOntoFrame(
        byte[] rgba,
        int rasterWidth,
        QrCode qr,
        int size,
        int insetModules,
        int pixelWidth)
    {
        // Common denominator for all module-grid coordinates, in pixels.
        long denom = SvgBadgeRenderer.FrameViewboxWidth * (long)(size + 2 * insetModules);

        long NumX(int k) =>
            pixelWidth
            * ((long)SvgBadgeRenderer.FrameQrBoxX * (size + 2 * insetModules)
                + (long)SvgBadgeRenderer.FrameQrBoxSize * (insetModules + k));

        long NumY(int k) =>
            pixelWidth
            * ((long)SvgBadgeRenderer.FrameQrBoxY * (size + 2 * insetModules)
                + (long)SvgBadgeRenderer.FrameQrBoxSize * (insetModules + k));

        // Round half up; edges are >= 2px apart (modulePx >= 3), so never degenerate.
        int Snap(long num) => (int)((2 * num + denom) / (2 * denom));

        int[] edgesX = new int[size + 1];
        int[] edgesY = new int[size + 1];
        for (int k = 0; k <= size; k++)
        {
            edgesX[k] = Snap(NumX(k));
            edgesY[k] = Snap(NumY(k));
        }

        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                if (SvgBadgeRenderer.IsFinderModule(row, col, size))
                {
                    continue;
                }

                if (!qr.GetModule(col, row))
                {
                    continue;
                }

                FillBlack(rgba, rasterWidth, edgesX[col], edgesY[row], edgesX[col + 1], edgesY[row + 1]);
            }
        }

        int lastFinderOrigin = size - SvgBadgeRenderer.FinderSize;
        RenderFinder(0, 0);
        RenderFinder(lastFinderOrigin, 0);
        RenderFinder(0, lastFinderOrigin);

        void RenderFinder(int moduleX, int moduleY)
        {
            // Q units: 1/(QPerPixel * denom) of a pixel. All geometry below is integer in Q.
            long qPerPixel = QPerPixel * denom;
            long moduleQ = (long)SvgBadgeRenderer.FrameQrBoxSize * pixelWidth * QPerPixel;
            long radiusUnit = (long)SvgBadgeRenderer.FrameQrBoxSize * pixelWidth * QPerPixel / RadiusDenom;
            var outer = new RoundedRect(
                NumX(moduleX) * QPerPixel,
                NumY(moduleY) * QPerPixel,
                SvgBadgeRenderer.FinderSize * moduleQ,
                OuterRadius80ths * radiusUnit);
            var inner = new RoundedRect(
                outer.X0 + moduleQ,
                outer.Y0 + moduleQ,
                (SvgBadgeRenderer.FinderSize - 2) * moduleQ,
                InnerRadius80ths * radiusUnit);
            var dot = new RoundedRect(
                outer.X0 + 2 * moduleQ,
                outer.Y0 + 2 * moduleQ,
                (SvgBadgeRenderer.FinderSize - 4) * moduleQ,
                DotRadius80ths * radiusUnit);

            bool BlackAt(long xQ, long yQ)
            {
                if (!InsideRoundedRect(xQ, yQ, outer))
                {
                    return false;
                }

                if (!InsideRoundedRect(xQ, yQ, inner))
                {
                    return true; // the ring
                }

                return InsideRoundedRect(xQ, yQ, dot);
            }

            int pxLo = (int)(outer.X0 / qPerPixel);
            int pxHi = (int)((outer.X0 + outer.Size + qPerPixel - 1) / qPerPixel);
            int pyLo = (int)(outer.Y0 / qPerPixel);
            int pyHi = (int)((outer.Y0 + outer.Size + qPerPixel - 1) / qPerPixel);

            for (int py = pyLo; py < pyHi; py++)
            {
                for (int px = pxLo; px < pxHi; px++)
                {
                    long left = px * qPerPixel;
                    long right = (px + 1) * qPerPixel;
                    long top = py * qPerPixel;
                    long bottom = (py + 1) * qPerPixel;
                    bool corner = BlackAt(left, top);
                    int count;
                    if (BlackAt(right, top) == corner
                        && BlackAt(left, bottom) == corner
                        && BlackAt(right, bottom) == corner)
                    {
                        // Uniform pixel: features are >= modulePx (>= 3px) thick, so a
                        // pixel whose four corners agree is interior, not a boundary sliver.
                        count = corner ? SubsampleCount : 0;
                    }
                    else
                    {
                        count = 0;
                        for (int sy = 0; sy < Subsamples; sy++)
                        {
                            long yQ = ((long)py * QPerPixel + (2 * sy + 1) * QPerSubsampleStep) * denom;
                            for (int sx = 0; sx < Subsamples; sx++)
                            {
                                long xQ = ((long)px * QPerPixel + (2 * sx + 1) * QPerSubsampleStep) * denom;
                                if (BlackAt(xQ, yQ))
                                {
                                    count++;
                                }
                            }
                        }
                    }

                    if (count == 0)
                    {
                        continue;
                    }

                    // Black at the coverage as straight alpha, rounded half up; the QR
                    // box is transparent, so the host document shows through the edge pixels.
                    byte alpha = (byte)((510 * count + SubsampleCount) / (2 * SubsampleCount));
                    int offset = (py * rasterWidth + px) * 4;
                    rgba[offset] = 0;
                    rgba[offset + 1] = 0;
                    rgba[offset + 2] = 0;
                    rgba[offset + 3] = alpha;
                }
            }
        }
    }

    /// <summary>Inclusive point-in-rounded-square test; every argument is in Q units.</summary>
    private static bool InsideRoundedRect(long xQ, long yQ, RoundedRect rect)
    {
        if (xQ < rect.X0 || xQ > rect.X0 + rect.Size || yQ < rect.Y0 || yQ > rect.Y0 + rect.Size)
        {
            return false;
        }

        long dx = 0;
        if (xQ < rect.X0 + rect.Radius)
        {
            dx = rect.X0 + rect.Radius - xQ;
        }
        else if (xQ > rect.X0 + rect.Size - rect.Radius)
        {
            dx = xQ - (rect.X0 + rect.Size - rect.Radius);
        }

        long dy = 0;
        if (yQ < rect.Y0 + rect.Radius)
        {
            dy = rect.Y0 + rect.Radius - yQ;
        }
        else if (yQ > rect.Y0 + rect.Size - rect.Radius)
        {
            dy = yQ - (rect.Y0 + rect.Size - rect.Radius);
        }

        if (dx == 0 || dy == 0)
        {
            return true;
        }

        return dx * dx + dy * dy <= rect.Radius * rect.Radius;
    }

    private static void FillBlack(byte[] rgba, int rasterWidth, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
        {
            int offset = (y * rasterWidth + x0) * 4;
            for (int x = x0; x < x1; x++)
            {
                rgba[offset] = 0;
                rgba[offset + 1] = 0;
                rgba[offset + 2] = 0;
                rgba[offset + 3] = 255;
                offset += 4;
            }
        }
    }

    private readonly struct RoundedRect
    {
        internal RoundedRect(long x0, long y0, long size, long radius)
        {
            X0 = x0;
            Y0 = y0;
            Size = size;
            Radius = radius;
        }

        internal long X0 { get; }

        internal long Y0 { get; }

        internal long Size { get; }

        internal long Radius { get; }
    }
}
