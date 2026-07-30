using System.IO.Compression;

namespace Verifiabl.Internal;

/// <summary>
/// Minimal, dependency-free PNG encoder for the composited QR badge raster,
/// mirroring the Node SDK's pngEncode.ts. The badge is a low-colour image, so
/// it encodes losslessly as an 8-bit palette PNG (colour type 3) with a tRNS
/// chunk for the rounded-corner alpha; if a raster ever exceeds 256 distinct
/// colours we fall back to truecolour RGBA (colour type 6). PNG bytes are not
/// byte-identical across SDKs (different DEFLATE encoders); the decoded raster
/// is, which is the cross-SDK contract.
/// </summary>
internal static class PngEncoder
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private const int MaxPalette = 256;

    private static readonly uint[] CrcTable = BuildCrcTable();

    internal static byte[] Encode(byte[] rgba, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Raster dimensions must be positive.", nameof(width));
        }

        // long guard so an oversized raster fails the length check rather than
        // wrapping to a value that spuriously matches rgba.Length.
        if (rgba.LongLength != (long)width * height * 4)
        {
            throw new ArgumentException("Raster length does not match width * height * 4.", nameof(rgba));
        }

        Palette? palette = BuildPalette(rgba, width, height);
        return palette is null
            ? EncodeTruecolor(rgba, width, height)
            : EncodeIndexed(palette, width, height);
    }

    private sealed class Palette
    {
        internal Palette(byte[] rgb, List<byte> alpha, byte[] indices)
        {
            Rgb = rgb;
            Alpha = alpha;
            Indices = indices;
        }

        /// <summary>3 bytes per entry, in index order.</summary>
        internal byte[] Rgb { get; }

        /// <summary>One alpha per entry, in index order.</summary>
        internal List<byte> Alpha { get; }

        /// <summary>One palette index per pixel.</summary>
        internal byte[] Indices { get; }
    }

    /// <summary>Build an indexed palette, or null when the image has more than 256 colours.</summary>
    private static Palette? BuildPalette(byte[] rgba, int width, int height)
    {
        int pixelCount = width * height;
        var map = new Dictionary<uint, int>();
        byte[] indices = new byte[pixelCount];
        var rgb = new List<byte>();
        var alpha = new List<byte>();

        for (int p = 0; p < pixelCount; p++)
        {
            int i = p * 4;
            uint key = ((uint)rgba[i] << 24)
                | ((uint)rgba[i + 1] << 16)
                | ((uint)rgba[i + 2] << 8)
                | rgba[i + 3];
            if (!map.TryGetValue(key, out int index))
            {
                if (alpha.Count >= MaxPalette)
                {
                    return null;
                }

                index = alpha.Count;
                map.Add(key, index);
                rgb.Add(rgba[i]);
                rgb.Add(rgba[i + 1]);
                rgb.Add(rgba[i + 2]);
                alpha.Add(rgba[i + 3]);
            }

            indices[p] = (byte)index;
        }

        return new Palette([.. rgb], alpha, indices);
    }

    private static byte[] EncodeIndexed(Palette palette, int width, int height)
    {
        int stride = width + 1; // one filter byte per scanline
        byte[] raw = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * stride;
            raw[rowStart] = 0; // filter: None
            Array.Copy(palette.Indices, y * width, raw, rowStart + 1, width);
        }

        // tRNS may omit trailing fully-opaque entries (decoders assume 255).
        int trnsLength = palette.Alpha.Count;
        while (trnsLength > 0 && palette.Alpha[trnsLength - 1] == 255)
        {
            trnsLength--;
        }

        using var png = new MemoryStream();
        png.Write(Signature, 0, Signature.Length);
        WriteChunk(png, "IHDR", Ihdr(width, height, colorType: 3));
        WriteChunk(png, "PLTE", palette.Rgb);
        if (trnsLength > 0)
        {
            byte[] trns = new byte[trnsLength];
            for (int i = 0; i < trnsLength; i++)
            {
                trns[i] = palette.Alpha[i];
            }

            WriteChunk(png, "tRNS", trns);
        }

        WriteChunk(png, "IDAT", ZlibCompress(raw));
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] EncodeTruecolor(byte[] rgba, int width, int height)
    {
        int stride = width * 4 + 1;
        byte[] raw = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * stride;
            raw[rowStart] = 0; // filter: None
            Array.Copy(rgba, y * width * 4, raw, rowStart + 1, width * 4);
        }

        using var png = new MemoryStream();
        png.Write(Signature, 0, Signature.Length);
        WriteChunk(png, "IHDR", Ihdr(width, height, colorType: 6));
        WriteChunk(png, "IDAT", ZlibCompress(raw));
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] Ihdr(int width, int height, byte colorType)
    {
        byte[] data = new byte[13];
        WriteUInt32(data, 0, (uint)width);
        WriteUInt32(data, 4, (uint)height);
        data[8] = 8; // bit depth
        data[9] = colorType;
        data[10] = 0; // compression
        data[11] = 0; // filter
        data[12] = 0; // interlace
        return data;
    }

    private static void WriteChunk(MemoryStream png, string type, byte[] data)
    {
        byte[] header = new byte[8];
        WriteUInt32(header, 0, (uint)data.Length);
        for (int i = 0; i < 4; i++)
        {
            header[4 + i] = (byte)type[i];
        }

        png.Write(header, 0, 8);
        png.Write(data, 0, data.Length);

        uint crc = 0xFFFFFFFF;
        for (int i = 4; i < 8; i++)
        {
            crc = CrcTable[(crc ^ header[i]) & 0xFF] ^ (crc >> 8);
        }

        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        byte[] crcBytes = new byte[4];
        WriteUInt32(crcBytes, 0, crc ^ 0xFFFFFFFF);
        png.Write(crcBytes, 0, 4);
    }

    /// <summary>Wrap DeflateStream output as a zlib stream (2-byte header + adler32 trailer).</summary>
    private static byte[] ZlibCompress(byte[] raw)
    {
        using var buffer = new MemoryStream();
        buffer.WriteByte(0x78);
        buffer.WriteByte(0x9C);
        using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }

        uint adler = Adler32(raw);
        byte[] trailer = new byte[4];
        WriteUInt32(trailer, 0, adler);
        buffer.Write(trailer, 0, 4);
        return buffer.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        const uint modulus = 65521;
        uint a = 1;
        uint b = 0;
        int offset = 0;
        while (offset < data.Length)
        {
            // 5552 is the largest run that cannot overflow the 32-bit accumulators.
            int run = Math.Min(5552, data.Length - offset);
            for (int i = 0; i < run; i++)
            {
                a += data[offset + i];
                b += a;
            }

            a %= modulus;
            b %= modulus;
            offset += run;
        }

        return (b << 16) | a;
    }

    private static void WriteUInt32(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) == 1 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
