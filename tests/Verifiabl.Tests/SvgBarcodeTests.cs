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

    private static string RealisticCiphertext()
    {
        string pii = Pii.Format(new PiiFields
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
        return Internal.Base64Url.Encode(bytes);
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
        Assert.Equal(755, result.Height);
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"480\" height=\"755\" ", result.Svg);
        Assert.Contains("viewBox=\"0 0 96 151\"", result.Svg);
        Assert.Contains("aria-label=\"Secured by Verifiabl verification barcode\"", result.Svg);
        // White frame body, grey border, navy header.
        Assert.Contains("<rect x=\"1\" y=\"1\" width=\"94\" height=\"149\" rx=\"7\" fill=\"#FFFFFF\"/>", result.Svg);
        Assert.Contains("stroke=\"#ADADAD\"", result.Svg);
        Assert.Contains("fill=\"#010A4F\"", result.Svg);
        // Three rounded finder patterns.
        Assert.Equal(3, Regex.Matches(result.Svg, "fill-rule=\"evenodd\"").Count);
    }

    [Fact]
    public void KeepsFrameGeometryFixedAsPayloadSizeChanges()
    {
        BarcodeSvgResult small = VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, "aaaa"));
        BarcodeSvgResult large = VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, RealisticCiphertext()));

        Assert.Equal(small.Width, large.Width);
        Assert.Equal(small.Height, large.Height);
        Assert.Contains("viewBox=\"0 0 96 151\"", small.Svg);
        Assert.Contains("viewBox=\"0 0 96 151\"", large.Svg);
    }

    [Fact]
    public void RespectsCustomWidths()
    {
        BarcodeSvgResult result = VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, RealisticCiphertext()),
            new BarcodeSvgOptions { Width = 720 });

        Assert.Equal(720, result.Width);
        Assert.Equal(1132.5, result.Height);
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"720\" height=\"1132.5\" ", result.Svg);
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
    public void RejectsLowAsACeilingInsteadOfSilentlyForcingIt()
    {
        Assert.Throws<ArgumentException>(() => VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, RealisticCiphertext()),
            new BarcodeSvgOptions { MaxErrorCorrection = BarcodeErrorCorrectionLevel.Low }));
    }

    [Fact]
    public void HardErrorsWhenPiiCannotFitTheFixedFrame()
    {
        // ~2900 characters still encodes as a QR code, but not at a scannable
        // module size inside the fixed frame at width 480.
        string huge = new('a', 2900);

        var exception = Assert.Throws<InvalidOperationException>(
            () => VerifiablBarcode.CreateSvg(new BarcodeParts(Reference, huge)));

        Assert.Contains("Shorten the PII fields", exception.Message);
    }

    [Fact]
    public void ThrowsAClearErrorWhenContentExceedsQrCapacityEntirely()
    {
        // Beyond version 40 byte capacity at every error-correction level.
        string beyondCapacity = new('a', 9000);

        var exception = Assert.Throws<InvalidOperationException>(
            () => VerifiablBarcode.CreateSvg(new BarcodeParts(Reference, beyondCapacity)));

        Assert.Contains("any error-correction level", exception.Message);
    }

    [Fact]
    public void SvgModulesMatchTheQrMatrixAndDecodeToTheScanUrl()
    {
        BarcodeSvgResult result = VerifiablBarcode.CreateSvg(
            new BarcodeParts(Reference, RealisticCiphertext()),
            new BarcodeSvgOptions { Format = BarcodePayloadFormat.V1 });

        // Rebuild the same QR matrix the renderer used.
        QrCode qr = QrCode.EncodeTextAdvanced(
            result.Content,
            QrCode.Ecc.Medium,
            boostEcl: false);
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
