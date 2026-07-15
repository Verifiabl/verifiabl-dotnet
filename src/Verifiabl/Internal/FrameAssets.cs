using System.Collections.Concurrent;
using System.IO.Compression;

namespace Verifiabl.Internal;

/// <summary>
/// Pre-rasterised badge frames for the PNG compositor: everything in the badge
/// except the QR content, baked per supported pixel width by the Node SDK's
/// scripts/bake-frames.mjs and embedded as VFR1 containers (RGBA palette plus
/// raw-deflated pixel indices).
/// </summary>
internal static class FrameAssets
{
    internal static readonly int[] SupportedPixelWidths = [480, 720, 960, 1440];

    private static readonly ConcurrentDictionary<int, ParsedFrame> Cache = new();

    internal static bool IsSupported(int pixelWidth)
    {
        return Array.IndexOf(SupportedPixelWidths, pixelWidth) >= 0;
    }

    internal static ParsedFrame Load(int pixelWidth)
    {
        return Cache.GetOrAdd(pixelWidth, Parse);
    }

    /// <summary>A fresh straight-alpha RGBA raster of the frame; the compositor mutates it.</summary>
    internal static byte[] ExpandRgba(ParsedFrame frame)
    {
        byte[] rgba = new byte[frame.Width * frame.Height * 4];
        for (int p = 0; p < frame.Indices.Length; p++)
        {
            int entry = frame.Indices[p] * 4;
            int offset = p * 4;
            rgba[offset] = frame.Palette[entry];
            rgba[offset + 1] = frame.Palette[entry + 1];
            rgba[offset + 2] = frame.Palette[entry + 2];
            rgba[offset + 3] = frame.Palette[entry + 3];
        }

        return rgba;
    }

    private static ParsedFrame Parse(int pixelWidth)
    {
        return ParseContainer(ReadResource($"Verifiabl.Assets.frame-{pixelWidth}.vfr1"), pixelWidth);
    }

    /// <summary>
    /// Decode and validate one VFR1 frame container. Internal so the package's
    /// own tests can exercise the corruption guards; production callers reach
    /// frames through <see cref="Load"/>.
    /// </summary>
    internal static ParsedFrame ParseContainer(byte[] container, int expectedWidth)
    {
        if (container.Length < 14
            || container[0] != (byte)'V' || container[1] != (byte)'F'
            || container[2] != (byte)'R' || container[3] != (byte)'1')
        {
            throw new InvalidOperationException("Corrupt frame asset: bad magic.");
        }

        int width = ReadUInt16(container, 4);
        int height = ReadUInt16(container, 6);
        int paletteCount = ReadUInt16(container, 8);
        // width is validated against the expected size, so width * height cannot
        // overflow int (max 1440 * 65535). The palette encoder never exceeds 256.
        if (width != expectedWidth || height <= 0)
        {
            throw new InvalidOperationException("Corrupt frame asset: unexpected dimensions.");
        }

        if (paletteCount is < 1 or > 256)
        {
            throw new InvalidOperationException("Corrupt frame asset: implausible palette size.");
        }

        const int paletteStart = 10;
        int deflatedLengthOffset = paletteStart + paletteCount * 4;
        if (deflatedLengthOffset + 4 > container.Length)
        {
            throw new InvalidOperationException("Corrupt frame asset: truncated header.");
        }

        int deflatedLength = checked((int)ReadUInt32(container, deflatedLengthOffset));
        int deflatedStart = deflatedLengthOffset + 4;
        if (deflatedStart + deflatedLength != container.Length)
        {
            throw new InvalidOperationException("Corrupt frame asset: length mismatch.");
        }

        byte[] palette = new byte[paletteCount * 4];
        Array.Copy(container, paletteStart, palette, 0, palette.Length);

        byte[] indices = new byte[width * height];
        using (var deflated = new MemoryStream(container, deflatedStart, deflatedLength, writable: false))
        using (var inflate = new DeflateStream(deflated, CompressionMode.Decompress))
        {
            int read = 0;
            while (read < indices.Length)
            {
                int chunk = inflate.Read(indices, read, indices.Length - read);
                if (chunk <= 0)
                {
                    break;
                }

                read += chunk;
            }

            if (read != indices.Length || inflate.ReadByte() != -1)
            {
                throw new InvalidOperationException("Corrupt frame asset: pixel count mismatch.");
            }
        }

        // Every index must address a palette entry, so ExpandRgba can never read
        // past the palette (which would throw an opaque IndexOutOfRangeException).
        foreach (byte index in indices)
        {
            if (index >= paletteCount)
            {
                throw new InvalidOperationException("Corrupt frame asset: palette index out of range.");
            }
        }

        return new ParsedFrame(width, height, palette, indices);
    }

    private static byte[] ReadResource(string name)
    {
        using Stream? stream = typeof(FrameAssets).Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            throw new InvalidOperationException($"Missing embedded frame asset '{name}'.");
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static int ReadUInt16(byte[] data, int offset)
    {
        return (data[offset] << 8) | data[offset + 1];
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24)
            | ((uint)data[offset + 1] << 16)
            | ((uint)data[offset + 2] << 8)
            | data[offset + 3];
    }

    internal readonly struct ParsedFrame
    {
        internal ParsedFrame(int width, int height, byte[] palette, byte[] indices)
        {
            Width = width;
            Height = height;
            Palette = palette;
            Indices = indices;
        }

        internal int Width { get; }

        internal int Height { get; }

        /// <summary>RGBA (straight alpha), 4 bytes per palette entry.</summary>
        internal byte[] Palette { get; }

        /// <summary>One palette index per pixel, row-major.</summary>
        internal byte[] Indices { get; }
    }
}
