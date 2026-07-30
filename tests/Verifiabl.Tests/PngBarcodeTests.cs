using System.IO.Compression;
using Verifiabl.Internal;
using Xunit;
using ZXing;

namespace Verifiabl.Tests;

public class PngBarcodeTests
{
    private const string Reference = "u0FE9WLIS7GYKQnpJPygBw";

    private static string Ciphertext()
    {
        return string.Concat(Enumerable.Repeat("Ab3", 80)) + "Zz19-_";
    }

    private static BarcodeParts Parts()
    {
        return new BarcodeParts(Reference, Ciphertext());
    }

    [Theory]
    [InlineData(480, 755)]
    [InlineData(720, 1133)]
    [InlineData(960, 1510)]
    [InlineData(1440, 2265)]
    public void RendersAPngAtEachSupportedPixelWidth(int pixelWidth, int expectedHeight)
    {
        BarcodePngResult result = VerifiablBarcode.CreatePng(Parts(), pixelWidth: pixelWidth);

        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Assert.Equal(signature, result.Png.Take(8).ToArray());
        Assert.Equal(pixelWidth, result.Width);
        Assert.Equal(expectedHeight, result.Height);
    }

    [Fact]
    public void IsDeterministicByteForByte()
    {
        BarcodePngResult first = VerifiablBarcode.CreatePng(Parts());
        BarcodePngResult second = VerifiablBarcode.CreatePng(Parts());

        Assert.Equal(first.Png, second.Png);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-720)]
    [InlineData(479)]
    [InlineData(640)]
    [InlineData(1920)]
    public void RejectsUnsupportedPixelWidths(int pixelWidth)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => VerifiablBarcode.CreatePng(Parts(), pixelWidth: pixelWidth));

        Assert.Contains("480, 720, 960, 1440", error.Message);
    }

    [Fact]
    public void ReportsTheSameContentAndMetadataAsTheSvgRenderer()
    {
        BarcodeSvgResult svg = VerifiablBarcode.CreateSvg(
            Parts(),
            new BarcodeSvgOptions { Width = 720 });
        BarcodePngResult png = VerifiablBarcode.CreatePng(Parts());

        Assert.Equal(svg.Content, png.Content);
        Assert.Equal(svg.ErrorCorrectionLevel, png.ErrorCorrectionLevel);
        Assert.Equal(svg.ModulePx, png.ModulePx);
        Assert.Equal(svg.Degraded, png.Degraded);
    }

    [Fact]
    public void CompositedRasterDecodesWithAnIndependentReader()
    {
        PngBadgeRenderer.CompositedBadge badge = PngBadgeRenderer.Compose(
            Parts(),
            new BarcodeSvgOptions(),
            720);

        var reader = new BarcodeReaderGeneric
        {
            Options =
            {
                TryHarder = true,
                PossibleFormats = [BarcodeFormat.QR_CODE],
            },
        };
        Result? decoded = reader.Decode(new RGBLuminanceSource(
            badge.Rgba,
            badge.Width,
            badge.Height,
            RGBLuminanceSource.BitmapFormat.RGBA32));

        Assert.NotNull(decoded);
        Assert.Equal(badge.Content, decoded!.Text);
    }

    [Fact]
    public void PaletteEncodingRoundTripsToTheCompositedRaster()
    {
        PngBadgeRenderer.CompositedBadge badge = PngBadgeRenderer.Compose(
            Parts(),
            new BarcodeSvgOptions(),
            480);
        byte[] png = PngEncoder.Encode(badge.Rgba, badge.Width, badge.Height);

        DecodedPng decoded = DecodePng(png);
        Assert.Equal(3, decoded.ColorType);
        Assert.Equal(badge.Width, decoded.Width);
        Assert.Equal(badge.Height, decoded.Height);
        Assert.Equal(badge.Rgba, decoded.Rgba);
    }

    [Fact]
    public void FallsBackToTruecolourAboveThePaletteLimit()
    {
        // A synthetic gradient with far more than 256 distinct colours.
        const int size = 64;
        byte[] rgba = new byte[size * size * 4];
        for (int p = 0; p < size * size; p++)
        {
            rgba[p * 4] = (byte)(p % 256);
            rgba[p * 4 + 1] = (byte)(p / 256 * 41);
            rgba[p * 4 + 2] = (byte)(p % 251);
            rgba[p * 4 + 3] = 255;
        }

        byte[] png = PngEncoder.Encode(rgba, size, size);

        DecodedPng decoded = DecodePng(png);
        Assert.Equal(6, decoded.ColorType);
        Assert.Equal(rgba, decoded.Rgba);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(-1, 4)]
    [InlineData(4, 0)]
    public void RejectsNonPositiveDimensions(int width, int height)
    {
        Assert.Throws<ArgumentException>(() => PngEncoder.Encode(new byte[16], width, height));
    }

    [Fact]
    public void RejectsARasterLengthThatDoesNotMatchTheDimensions()
    {
        Assert.Throws<ArgumentException>(() => PngEncoder.Encode(new byte[15], 2, 2));
    }

    /// <summary>
    /// Minimal PNG reader for the encoder's own output (filter None only),
    /// verifying chunk CRCs and the zlib adler32 trailer along the way.
    /// </summary>
    private static DecodedPng DecodePng(byte[] png)
    {
        int width = 0;
        int height = 0;
        int colorType = -1;
        byte[]? plte = null;
        byte[]? trns = null;
        using var idat = new MemoryStream();

        int offset = 8; // skip signature
        while (offset < png.Length)
        {
            int length = ReadInt32(png, offset);
            string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            uint expectedCrc = (uint)ReadInt32(png, offset + 8 + length);
            Assert.Equal(expectedCrc, Crc32(png, offset + 4, length + 4));

            int dataStart = offset + 8;
            switch (type)
            {
                case "IHDR":
                    width = ReadInt32(png, dataStart);
                    height = ReadInt32(png, dataStart + 4);
                    Assert.Equal(8, png[dataStart + 8]); // bit depth
                    colorType = png[dataStart + 9];
                    break;
                case "PLTE":
                    plte = png.Skip(dataStart).Take(length).ToArray();
                    break;
                case "tRNS":
                    trns = png.Skip(dataStart).Take(length).ToArray();
                    break;
                case "IDAT":
                    idat.Write(png, dataStart, length);
                    break;
                default:
                    break;
            }

            offset = dataStart + length + 4;
        }

        byte[] zlib = idat.ToArray();
        Assert.Equal(0x78, zlib[0]);
        byte[] raw;
        using (var inflate = new DeflateStream(
            new MemoryStream(zlib, 2, zlib.Length - 6, writable: false),
            CompressionMode.Decompress))
        using (var buffer = new MemoryStream())
        {
            inflate.CopyTo(buffer);
            raw = buffer.ToArray();
        }

        Assert.Equal((uint)ReadInt32(zlib, zlib.Length - 4), Adler32(raw));

        byte[] rgba = new byte[width * height * 4];
        int bytesPerPixel = colorType == 3 ? 1 : 4;
        int stride = width * bytesPerPixel + 1;
        Assert.Equal(stride * height, raw.Length);
        if (colorType == 3)
        {
            // PNG requires PLTE ahead of IDAT, so an indexed image always has one here.
            Assert.NotNull(plte);
        }

        for (int y = 0; y < height; y++)
        {
            Assert.Equal(0, raw[y * stride]); // filter: None
            for (int x = 0; x < width; x++)
            {
                int source = y * stride + 1 + x * bytesPerPixel;
                int target = (y * width + x) * 4;
                if (colorType == 3)
                {
                    int index = raw[source];
                    rgba[target] = plte![index * 3];
                    rgba[target + 1] = plte[index * 3 + 1];
                    rgba[target + 2] = plte[index * 3 + 2];
                    rgba[target + 3] = trns is not null && index < trns.Length ? trns[index] : (byte)255;
                }
                else
                {
                    rgba[target] = raw[source];
                    rgba[target + 1] = raw[source + 1];
                    rgba[target + 2] = raw[source + 2];
                    rgba[target + 3] = raw[source + 3];
                }
            }
        }

        return new DecodedPng(width, height, colorType, rgba);
    }

    private static int ReadInt32(byte[] data, int offset)
    {
        return (data[offset] << 24)
            | (data[offset + 1] << 16)
            | (data[offset + 2] << 8)
            | data[offset + 3];
    }

    private static uint Crc32(byte[] data, int offset, int length)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < length; i++)
        {
            uint c = (crc ^ data[offset + i]) & 0xFF;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) == 1 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            crc = c ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint Adler32(byte[] data)
    {
        const uint modulus = 65521;
        uint a = 1;
        uint b = 0;
        foreach (byte value in data)
        {
            a = (a + value) % modulus;
            b = (b + a) % modulus;
        }

        return (b << 16) | a;
    }

    private sealed class DecodedPng
    {
        internal DecodedPng(int width, int height, int colorType, byte[] rgba)
        {
            Width = width;
            Height = height;
            ColorType = colorType;
            Rgba = rgba;
        }

        internal int Width { get; }

        internal int Height { get; }

        internal int ColorType { get; }

        internal byte[] Rgba { get; }
    }
}
