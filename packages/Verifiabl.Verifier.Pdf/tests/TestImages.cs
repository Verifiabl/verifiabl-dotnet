using System.IO.Compression;
using System.Text;
using Verifiabl.Verifier.Extraction;
using ZXing;
using ZXing.Common;

namespace Verifiabl.Verifier.Pdf.Tests;

/// <summary>Renders QR codes as greyscale pixels, PNG files and stand-in JPEG files.</summary>
internal static class TestImages
{
    internal sealed class Gray
    {
        internal Gray(byte[] pixels, int width, int height)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
        }

        internal byte[] Pixels { get; }

        internal int Width { get; }

        internal int Height { get; }

        internal QrImage ToImage() => QrImage.FromGray8(Pixels, Width, Height);
    }

    internal static Gray RenderQr(string text, int size = 300)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = size, Height = size, Margin = 4 },
        };
        ZXing.Rendering.PixelData data = writer.Write(text);

        // ZXing writes BGRA; black and white pixels have equal channels.
        byte[] gray = new byte[data.Width * data.Height];
        for (int pixel = 0; pixel < gray.Length; pixel++)
        {
            gray[pixel] = data.Pixels[pixel * 4];
        }

        return new Gray(gray, data.Width, data.Height);
    }

    internal static byte[] QrPng(string text, int size = 300) => Png(RenderQr(text, size));

    /// <summary>An 8-bit greyscale PNG with unfiltered rows.</summary>
    internal static byte[] Png(Gray image)
    {
        byte[] scanlines = new byte[image.Height * (image.Width + 1)];
        for (int row = 0; row < image.Height; row++)
        {
            Buffer.BlockCopy(image.Pixels, row * image.Width, scanlines, (row * (image.Width + 1)) + 1, image.Width);
        }

        byte[] header = new byte[13];
        WriteInt(header, 0, image.Width);
        WriteInt(header, 4, image.Height);
        header[8] = 8;

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], 0, 8);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", TestPdf.Zlib(scanlines));
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>
    /// A stand-in JPEG: a valid header declaring the size, with no image data.
    /// Tests pair it with a decoder that returns known pixels.
    /// </summary>
    internal static byte[] JpegHeaderOnly(int width, int height)
    {
        return
        [
            0xFF, 0xD8,
            0xFF, 0xC0, 0x00, 0x11, 0x08,
            (byte)(height >> 8), (byte)height,
            (byte)(width >> 8), (byte)width,
            0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
            0xFF, 0xD9,
        ];
    }

    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        byte[] length = new byte[4];
        WriteInt(length, 0, data.Length);
        byte[] typed = [.. Encoding.ASCII.GetBytes(type), .. data];
        byte[] crc = new byte[4];
        WriteInt(crc, 0, (int)Crc32(typed));
        png.Write(length, 0, 4);
        png.Write(typed, 0, typed.Length);
        png.Write(crc, 0, 4);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
