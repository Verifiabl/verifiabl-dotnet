using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Net.Codecrete.QrCodeGenerator;
using Xunit;
using ZXing;

namespace Verifiabl.Tests;

public class SvgBarcodeTests
{
    private const string Reference = "u0FE9WLIS7GYKQnpJPygBw";

    private static byte[] RealisticCiphertext()
    {
        string pii = Pii.FormatAustralian(new AustralianPiiFields
        {
            EmployeeName = "Jane A. Doe",
            Position = "Senior Developer",
            Department = "Engineering",
            EmployerAbn = "12345678901",
            Bsb = "062-000",
            AccountNumber = "12345678",
            AccountName = "Jane A Doe",
        });
        // Deterministic bytes with the exact length EncryptPii would emit (GCM
        // ciphertext length equals plaintext length). EncryptPii's random IV made
        // this fixture differ per run, and rare payloads rendered a matrix ZXing
        // failed to decode at this test's synthetic scale.
        byte[] bytes = new byte[Encoding.UTF8.GetByteCount(pii)];
        new Random(20260727).NextBytes(bytes);
        return bytes;
    }

    [Fact]
    public void EncodesTheScanUrlByDefault()
    {
        BarcodeSvgResult result = VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, RealisticCiphertext()));

        Assert.StartsWith($"https://v.verifiabl.io/v/{Reference}#2.", result.Content);
    }

    [Fact]
    public void UsesTheSandboxScanUrlForSandbox()
    {
        BarcodeSvgResult result = VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, RealisticCiphertext()),
            new BarcodeSvgOptions { Environment = VerifiablEnvironment.Sandbox });

        Assert.StartsWith("https://v.sandbox.verifiabl.io/v/", result.Content);
    }

    [Fact]
    public void RendersTheBrandedFrameGeometry()
    {
        BarcodeSvgResult result = VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, RealisticCiphertext()));

        Assert.Equal(480, result.Width);
        Assert.Equal(750, result.Height);
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"480\" height=\"750\" ", result.Svg);
        Assert.Contains("viewBox=\"0 0 96 150\"", result.Svg);
        Assert.Contains("aria-label=\"Secured by Verifiabl verification barcode\"", result.Svg);
        // No border: a full-width white ground under the header and the QR box,
        // then the navy header on top of it.
        Assert.Matches(
            "^<svg [^>]*><rect x=\"0\" y=\"39\" width=\"96\" height=\"111\" fill=\"#FFFFFF\"/><path d=\"M0 8C0 3\\.58172",
            result.Svg);
        Assert.Contains("fill=\"#010A4F\"", result.Svg);
        Assert.DoesNotContain("stroke=", result.Svg);
        Assert.DoesNotContain("<rect x=\"1\" y=\"1\"", result.Svg);
        // Three rounded finder patterns.
        Assert.Equal(3, Regex.Matches(result.Svg, "fill-rule=\"evenodd\"").Count);
    }

    [Fact]
    public void SpansTheFullBadgeWidthForEveryPayload()
    {
        foreach (BarcodeSvgResult result in new[] { new byte[1], RealisticCiphertext() }
                     .Select(ciphertext => VerifiablBarcode.CreateSvg(
                         new BarcodeParts(Reference, ciphertext))))
        {
            Assert.Contains("<g transform=\"translate(0 54)\"><g shape-rendering=\"crispEdges\">", result.Svg);
            Assert.Equal(Math.Round(480.0 / (17.0 + 4.0 * result.QrVersion), 2), result.ModulePx);
        }
    }

    [Fact]
    public void KeepsFrameGeometryFixedAsPayloadSizeChanges()
    {
        BarcodeSvgResult small = VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, new byte[3]));
        BarcodeSvgResult large = VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, RealisticCiphertext()));

        Assert.Equal(small.Width, large.Width);
        Assert.Equal(small.Height, large.Height);
        Assert.Contains("viewBox=\"0 0 96 150\"", small.Svg);
        Assert.Contains("viewBox=\"0 0 96 150\"", large.Svg);
    }

    [Fact]
    public void RespectsCustomWidths()
    {
        BarcodeSvgResult result = VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, RealisticCiphertext()),
            new BarcodeSvgOptions { Width = 720 });

        Assert.Equal(720, result.Width);
        Assert.Equal(1125, result.Height);
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"720\" height=\"1125\" ", result.Svg);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(479)]
    [InlineData(double.NaN)]
    public void RejectsInvalidWidths(double width)
    {
        Assert.Throws<ArgumentException>(() => VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, RealisticCiphertext()),
            new BarcodeSvgOptions { Width = width }));
    }

    [Fact]
    public void RendersTheCommonCasePristine()
    {
        BarcodeSvgResult result = VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, RealisticCiphertext()));

        Assert.Equal(BarcodeErrorCorrectionLevel.Medium, result.ErrorCorrectionLevel);
        Assert.False(result.Degraded);
        Assert.True(result.ModulePx >= 4, $"expected pristine module size, got {result.ModulePx}");
    }

    [Fact]
    public void QuartileCeilingYieldsADenserNonDegradedCode()
    {
        BarcodeParts parts = new(Reference, RealisticCiphertext());

        BarcodeSvgResult medium = VerifiablBarcode.CreateSvg(parts);
        BarcodeSvgResult quartile = VerifiablBarcode.CreateSvg(parts, new BarcodeSvgOptions
        {
            MaxErrorCorrection = BarcodeErrorCorrectionLevel.Quartile,
        });

        Assert.Equal(BarcodeErrorCorrectionLevel.Quartile, quartile.ErrorCorrectionLevel);
        Assert.False(quartile.Degraded);
        Assert.True(
            quartile.ModulePx < medium.ModulePx,
            "quartile should be denser than medium for the same payload");
    }

    [Fact]
    public void LowCeilingYieldsASparserNonDegradedCode()
    {
        BarcodeParts parts = new(Reference, RealisticCiphertext());

        BarcodeSvgResult medium = VerifiablBarcode.CreateSvg(parts);
        BarcodeSvgResult low = VerifiablBarcode.CreateSvg(parts, new BarcodeSvgOptions
        {
            MaxErrorCorrection = BarcodeErrorCorrectionLevel.Low,
        });

        Assert.Equal(BarcodeErrorCorrectionLevel.Low, low.ErrorCorrectionLevel);
        Assert.False(low.Degraded);
        Assert.True(
            low.ModulePx > medium.ModulePx,
            "low should be sparser than medium for the same payload");
    }

    [Fact]
    public void HardErrorsWhenPiiCannotFitTheFixedFrame()
    {
        // This ciphertext still encodes as a QR code, but not at a scannable
        // module size inside the fixed frame at width 480.
        byte[] huge = new byte[2_175];

        var exception = Assert.Throws<InvalidOperationException>(
            () => VerifiablBarcode.CreateSvg(new BarcodeParts(Reference, huge)));

        Assert.Contains("Shorten the PII fields", exception.Message);
    }

    [Fact]
    public void ThrowsAClearErrorWhenContentExceedsQrCapacityEntirely()
    {
        // Beyond version 40 byte capacity at every error-correction level.
        byte[] beyondCapacity = new byte[6_750];

        var exception = Assert.Throws<InvalidOperationException>(
            () => VerifiablBarcode.CreateSvg(new BarcodeParts(Reference, beyondCapacity)));

        Assert.Contains("any error-correction level", exception.Message);
    }

    [Fact]
    public void RendersTheExplicitVerticalLayoutExactlyAsTheDefault()
    {
        BarcodeParts parts = new(Reference, RealisticCiphertext());

        Assert.Equal(
            VerifiablBarcode.CreateSvg(parts).Svg,
            VerifiablBarcode.CreateSvg(parts, new BarcodeSvgOptions { Layout = BarcodeLayout.Vertical }).Svg);
    }

    [Fact]
    public void RendersTheHorizontalFrameGeometry()
    {
        BarcodeSvgResult result = VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, RealisticCiphertext()),
            new BarcodeSvgOptions { Layout = BarcodeLayout.Horizontal });

        Assert.Equal(940, result.Width);
        Assert.Equal(480, result.Height);
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"940\" height=\"480\" ", result.Svg);
        Assert.Contains("viewBox=\"0 0 188 96\"", result.Svg);
        // White under the 96-unit QR box and the vertical layout's 7-unit gap, then
        // the opaque 85-unit panel with the 70x80 design scaled to the QR box's height.
        Assert.Matches(
            "^<svg [^>]*><rect x=\"0\" y=\"0\" width=\"103\" height=\"96\" fill=\"#FFFFFF\"/>"
            + "<g transform=\"translate\\(103 0\\)\"><path d=\"M0 0H75\\.4C[^\"]*85 9\\.6V86\\.4C[^\"]*\" fill=\"#EDEFFF\"/>"
            + "<g transform=\"translate\\(0\\.5 0\\) scale\\(1\\.2\\)\" fill=\"#010A4F\">",
            result.Svg);
        Assert.Contains("<g transform=\"translate(0 0)\"><g shape-rendering=\"crispEdges\">", result.Svg);
        Assert.DoesNotContain("opacity", result.Svg);
        Assert.DoesNotContain("clipPath", result.Svg);
        Assert.DoesNotContain("M0 8C0 3.58172", result.Svg);
        Assert.Equal(3, Regex.Matches(result.Svg, "fill-rule=\"evenodd\"").Count);
    }

    [Fact]
    public void WidthDefaultsToTheLayoutMinimumUnlessSet()
    {
        var options = new BarcodeSvgOptions();
        Assert.Equal(480, options.Width);

        options.Layout = BarcodeLayout.Horizontal;
        Assert.Equal(940, options.Width);

        options.Width = 1425;
        options.Layout = BarcodeLayout.Vertical;
        Assert.Equal(1425, options.Width);
    }

    [Theory]
    [InlineData(480, 940)]
    [InlineData(720, 1410)]
    public void HorizontalBadgeRendersTheSameQrAsTheVerticalBadge(double verticalWidth, double width)
    {
        BarcodeParts parts = new(Reference, RealisticCiphertext());

        BarcodeSvgResult vertical = VerifiablBarcode.CreateSvg(
            parts,
            new BarcodeSvgOptions { Width = verticalWidth });
        BarcodeSvgResult horizontal = VerifiablBarcode.CreateSvg(
            parts,
            new BarcodeSvgOptions { Layout = BarcodeLayout.Horizontal, Width = width });

        Assert.Equal(vertical.Content, horizontal.Content);
        Assert.Equal(vertical.ErrorCorrectionLevel, horizontal.ErrorCorrectionLevel);
        Assert.Equal(vertical.QrVersion, horizontal.QrVersion);
        Assert.Equal(vertical.ModulePx, horizontal.ModulePx);
        Assert.Equal(vertical.Degraded, horizontal.Degraded);
    }

    [Fact]
    public void RejectsWidthsBelowTheHorizontalMinimum()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, RealisticCiphertext()),
            new BarcodeSvgOptions { Layout = BarcodeLayout.Horizontal, Width = 939 }));

        Assert.Contains("at least 940", error.Message);
    }

    [Fact]
    public void RejectsAnUnknownLayout()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, RealisticCiphertext()),
            new BarcodeSvgOptions { Layout = (BarcodeLayout)7 }));

        Assert.Contains("Layout must be Vertical or Horizontal", error.Message);
    }

    [Fact]
    public void HorizontalBadgeHardErrorsAtTheSamePayloadLengthAsTheVerticalBadge()
    {
        byte[] huge = new byte[2_175];

        var exception = Assert.Throws<InvalidOperationException>(
            () => VerifiablBarcode.CreateSvg(
                new BarcodeParts(Reference, huge),
                new BarcodeSvgOptions { Layout = BarcodeLayout.Horizontal }));

        Assert.Contains("at width 940", exception.Message);
    }

    [Fact]
    public void SvgModulesMatchTheQrMatrixAndDecodeToTheScanUrl()
    {
        BarcodeSvgResult result = VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, RealisticCiphertext()));

        // Rebuild the same mixed-mode QR matrix the renderer used.
        QrCode qr = Internal.SvgBadgeRenderer.EncodeV2Segments(
            result.Content,
            QrCode.Ecc.Medium);
        int size = qr.Size;

        // The data-module rects live in the crispEdges group, in QR-local
        // coordinates. Every dark non-finder module must have exactly one rect.
        string modulesGroup = ExtractModulesGroup(result.Svg);
        HashSet<(int Col, int Row)> rects = ParseModuleRects(modulesGroup, size);

        int expected = 0;
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                if (IsFinderRegion(row, col, size))
                {
                    continue;
                }

                if (qr.GetModule(col, row))
                {
                    expected++;
                    Assert.Contains((col, row), rects);
                }
            }
        }

        Assert.Equal(expected, rects.Count);

        // The matrix the SVG renders decodes back to the scan URL.
        Assert.Equal(result.Content, DecodeMatrix(qr));
    }

    private static string ExtractModulesGroup(string svg)
    {
        Match match = Regex.Match(
            svg,
            "<g shape-rendering=\"crispEdges\">(.*?)</g>",
            RegexOptions.Singleline);
        Assert.True(match.Success, "SVG must contain the data-module group");
        return match.Groups[1].Value;
    }

    private static HashSet<(int Col, int Row)> ParseModuleRects(string modulesGroup, int size)
    {
        MatchCollection matches = Regex.Matches(
            modulesGroup,
            "<rect x=\"([0-9.]+)\" y=\"([0-9.]+)\" width=\"([0-9.]+)\"");
        Assert.NotEmpty(matches);

        double moduleSize = double.Parse(matches[0].Groups[3].Value, CultureInfo.InvariantCulture);
        var rects = new HashSet<(int, int)>();
        foreach (Match match in matches)
        {
            double x = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            double y = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            int col = (int)Math.Round(x / moduleSize);
            int row = (int)Math.Round(y / moduleSize);
            Assert.InRange(col, 0, size - 1);
            Assert.InRange(row, 0, size - 1);
            rects.Add((col, row));
        }

        return rects;
    }

    private static bool IsFinderRegion(int row, int col, int size)
    {
        const int finder = 7;
        bool topLeft = row < finder && col < finder;
        bool topRight = row < finder && col >= size - finder;
        bool bottomLeft = row >= size - finder && col < finder;
        return topLeft || topRight || bottomLeft;
    }

    private static string DecodeMatrix(QrCode qr)
    {
        const int scale = 4;
        const int quiet = 4;
        int pixels = (qr.Size + quiet * 2) * scale;
        byte[] gray = new byte[pixels * pixels];
        for (int i = 0; i < gray.Length; i++)
        {
            gray[i] = 255;
        }

        for (int row = 0; row < qr.Size; row++)
        {
            for (int col = 0; col < qr.Size; col++)
            {
                if (!qr.GetModule(col, row))
                {
                    continue;
                }

                for (int dy = 0; dy < scale; dy++)
                {
                    int y = (row + quiet) * scale + dy;
                    for (int dx = 0; dx < scale; dx++)
                    {
                        int x = (col + quiet) * scale + dx;
                        gray[y * pixels + x] = 0;
                    }
                }
            }
        }

        var reader = new BarcodeReaderGeneric
        {
            Options =
            {
                TryHarder = true,
                PossibleFormats = [BarcodeFormat.QR_CODE],
            },
        };
        Result? decoded = reader.Decode(new RGBLuminanceSource(
            gray,
            pixels,
            pixels,
            RGBLuminanceSource.BitmapFormat.Gray8));

        Assert.NotNull(decoded);
        return decoded!.Text;
    }
}
