using System.IO.Compression;
using Verifiabl.Internal;
using Xunit;

namespace Verifiabl.Tests;

public class FrameAssetTests
{
    /// <summary>Build a VFR1 container from a palette size and pixel indices.</summary>
    private static byte[] Container(int width, int height, int paletteCount, byte[] indices)
    {
        using var deflated = new MemoryStream();
        using (var deflate = new DeflateStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(indices, 0, indices.Length);
        }

        byte[] body = deflated.ToArray();
        using var buffer = new MemoryStream();
        byte[] magic = System.Text.Encoding.ASCII.GetBytes("VFR1");
        buffer.Write(magic, 0, magic.Length);
        buffer.WriteByte((byte)(width >> 8));
        buffer.WriteByte((byte)width);
        buffer.WriteByte((byte)(height >> 8));
        buffer.WriteByte((byte)height);
        buffer.WriteByte((byte)(paletteCount >> 8));
        buffer.WriteByte((byte)paletteCount);
        buffer.Write(new byte[paletteCount * 4], 0, paletteCount * 4);
        buffer.WriteByte((byte)(body.Length >> 24));
        buffer.WriteByte((byte)(body.Length >> 16));
        buffer.WriteByte((byte)(body.Length >> 8));
        buffer.WriteByte((byte)body.Length);
        buffer.Write(body, 0, body.Length);
        return buffer.ToArray();
    }

    [Fact]
    public void AcceptsAWellFormedContainer()
    {
        FrameAssets.ParsedFrame frame = FrameAssets.ParseContainer(
            Container(3, 1, 2, [0, 1, 0]),
            expectedWidth: 3);

        Assert.Equal(3, frame.Width);
        Assert.Equal(1, frame.Height);
        Assert.Equal([0, 1, 0], frame.Indices);
    }

    [Fact]
    public void RejectsAPaletteIndexPastThePalette()
    {
        // index 2 with only 2 palette entries (valid 0..1) must fail fast so
        // ExpandRgba never reads past the palette.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => FrameAssets.ParseContainer(Container(2, 1, 2, [0, 2]), expectedWidth: 2));

        Assert.Contains("palette index out of range", error.Message);
    }

    [Fact]
    public void RejectsAnUnexpectedWidth()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => FrameAssets.ParseContainer(Container(3, 1, 1, [0, 0, 0]), expectedWidth: 720));

        Assert.Contains("unexpected dimensions", error.Message);
    }

    [Fact]
    public void RejectsAnImplausibleHeight()
    {
        // Height beyond 2x width would let a tampered asset force a huge
        // raster allocation before the pixel-count check runs.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => FrameAssets.ParseContainer(Container(2, 5, 1, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]), expectedWidth: 2));

        Assert.Contains("unexpected dimensions", error.Message);
    }

    [Fact]
    public void RejectsAPixelCountMismatch()
    {
        // Claim width 5 for a 2-pixel payload.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => FrameAssets.ParseContainer(Container(5, 1, 1, [0, 0]), expectedWidth: 5));

        Assert.Contains("pixel count mismatch", error.Message);
    }

    [Fact]
    public void EmbeddedFramesParseAndExpandForEverySupportedWidth()
    {
        foreach (int width in FrameAssets.SupportedPixelWidths)
        {
            FrameAssets.ParsedFrame frame = FrameAssets.Load(width);
            byte[] rgba = FrameAssets.ExpandRgba(frame);
            Assert.Equal(frame.Width * frame.Height * 4, rgba.Length);
        }
    }
}
