using System.IO.Compression;
using Verifiabl.Verifier.Extraction;

namespace Verifiabl.Verifier.Pdf.Internal;

/// <summary>
/// Reads the 8-bit, non-interlaced PNGs that PdfPig's <c>TryGetPng</c> writes
/// into pixels for the QR reader. Other PNG forms return null.
/// </summary>
internal static class PngPixels
{
    private const int SignatureLength = 8;
    private const int ChunkOverhead = 12;

    internal static QrImage? Read(byte[] png, long maxPixels)
    {
        if (png.Length < SignatureLength + ChunkOverhead + 13 || ReadInt(png, SignatureLength + 4) != 0x49484452)
        {
            return null;
        }

        int width = ReadInt(png, 16);
        int height = ReadInt(png, 20);
        int depth = png[24];
        int colourType = png[25];
        int interlace = png[28];
        int channels = colourType switch
        {
            0 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => 0,
        };
        if (width <= 0 || height <= 0 || (long)width * height > maxPixels || depth != 8 || channels == 0 || interlace != 0)
        {
            return null;
        }

        byte[]? scanlines = Inflate(ConcatenatedIdat(png), height * ((width * channels) + 1));
        return scanlines is null ? null : ToGray(scanlines, width, height, channels);
    }

    private static byte[] ConcatenatedIdat(byte[] png)
    {
        using var idat = new MemoryStream();
        int offset = SignatureLength;
        while (offset + ChunkOverhead <= png.Length)
        {
            int length = ReadInt(png, offset);
            if (length < 0 || offset + ChunkOverhead + length > png.Length)
            {
                break;
            }

            if (ReadInt(png, offset + 4) == 0x49444154)
            {
                idat.Write(png, offset + 8, length);
            }

            offset += ChunkOverhead + length;
        }

        return idat.ToArray();
    }

    /// <summary>Inflate zlib data to exactly <paramref name="expected"/> bytes, or null.</summary>
    private static byte[]? Inflate(byte[] zlib, int expected)
    {
        const int ZlibHeaderLength = 2;
        if (zlib.Length <= ZlibHeaderLength)
        {
            return null;
        }

        try
        {
            using var input = new MemoryStream(zlib, ZlibHeaderLength, zlib.Length - ZlibHeaderLength);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            byte[] output = new byte[expected];
            int filled = 0;
            int read;
            while (filled < expected && (read = deflate.Read(output, filled, expected - filled)) > 0)
            {
                filled += read;
            }

            return filled == expected ? output : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Undo PNG row filters and reduce each pixel to greyscale, which is all the QR reader needs.</summary>
    private static QrImage ToGray(byte[] scanlines, int width, int height, int channels)
    {
        int stride = width * channels;
        byte[] previous = new byte[stride];
        byte[] current = new byte[stride];
        byte[] gray = new byte[width * height];
        for (int row = 0; row < height; row++)
        {
            int start = row * (stride + 1);
            Unfilter(scanlines[start], scanlines, start + 1, current, previous, channels);
            for (int column = 0; column < width; column++)
            {
                gray[(row * width) + column] = Luminance(current, column * channels, channels);
            }

            (previous, current) = (current, previous);
        }

        return QrImage.FromGray8(gray, width, height);
    }

    private static void Unfilter(int filter, byte[] source, int offset, byte[] current, byte[] previous, int bpp)
    {
        for (int index = 0; index < current.Length; index++)
        {
            int left = index >= bpp ? current[index - bpp] : 0;
            int up = previous[index];
            int upLeft = index >= bpp ? previous[index - bpp] : 0;
            int predictor = filter switch
            {
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => 0,
            };
            current[index] = (byte)(source[offset + index] + predictor);
        }
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int estimate = left + up - upLeft;
        int toLeft = Math.Abs(estimate - left);
        int toUp = Math.Abs(estimate - up);
        int toUpLeft = Math.Abs(estimate - upLeft);
        if (toLeft <= toUp && toLeft <= toUpLeft)
        {
            return left;
        }

        return toUp <= toUpLeft ? up : upLeft;
    }

    private static byte Luminance(byte[] row, int offset, int channels)
    {
        // Transparent pixels read as white, as the core RGBA reader treats them.
        bool hasAlpha = channels is 2 or 4;
        if (hasAlpha && row[offset + channels - 1] == 0)
        {
            return byte.MaxValue;
        }

        if (channels <= 2)
        {
            return row[offset];
        }

        return (byte)(((row[offset] * 299) + (row[offset + 1] * 587) + (row[offset + 2] * 114)) / 1000);
    }

    private static int ReadInt(byte[] buffer, int offset) =>
        (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];
}
