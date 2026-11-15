using System.Runtime.InteropServices;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace Verifiabl.Verifier.Extraction.Images.Tests;

/// <summary>Encodes QR codes as image files for the decoder tests.</summary>
internal static class TestImages
{
    internal const string ScanLink = "https://v.sandbox.verifiabl.io/v/n5waC35dPCoRtMXW5_gJqw#2.JVSWY3DPO5XXE3DE";

    internal const string OtherScanLink = "https://v.sandbox.verifiabl.io/v/Q7xd2kLm9PqRsTuVwXyZ0a#2.MFRGGZDFMZTWQ2LK";

    internal const int Size = 300;

    /// <summary>A 1x1 GIF: a format Skia can decode but the SDK does not read.</summary>
    internal static readonly byte[] Gif =
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0xFF, 0xFF, 0xFF,
        0x00, 0x00, 0x00, 0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44,
        0x01, 0x00, 0x3B,
    ];

    /// <summary>A QR code as an image file, optionally with a transparent background.</summary>
    internal static byte[] QrFile(string text, SKEncodedImageFormat format, bool transparent = false)
    {
        (byte[] bgra, int width, int height) = RenderBgra(text);
        if (transparent)
        {
            // Light modules become fully transparent black, as some badge exports store them.
            for (int index = 0; index < bgra.Length; index += 4)
            {
                if (bgra[index] > 128)
                {
                    bgra[index] = bgra[index + 1] = bgra[index + 2] = bgra[index + 3] = 0;
                }
            }
        }

        return Encode(bgra, width, height, format);
    }

    /// <summary>Two codes side by side in one PNG, as two payslips in one photo.</summary>
    internal static byte[] TwoQrPng(string left, string right)
    {
        (byte[] first, int width, int height) = RenderBgra(left);
        (byte[] second, _, _) = RenderBgra(right);
        byte[] canvas = new byte[width * 2 * height * 4];
        for (int row = 0; row < height; row++)
        {
            Buffer.BlockCopy(first, row * width * 4, canvas, row * width * 2 * 4, width * 4);
            Buffer.BlockCopy(second, row * width * 4, canvas, ((row * width * 2) + width) * 4, width * 4);
        }

        return Encode(canvas, width * 2, height, SKEncodedImageFormat.Png);
    }

    /// <summary>A plain white PNG with no QR code.</summary>
    internal static byte[] BlankPng()
    {
        byte[] white = Enumerable.Repeat((byte)255, Size * Size * 4).ToArray();
        return Encode(white, Size, Size, SKEncodedImageFormat.Png);
    }

    /// <summary>A valid PNG whose header claims a huge image, with a correct header checksum.</summary>
    internal static byte[] PngClaiming(int width, int height)
    {
        byte[] png = QrFile(ScanLink, SKEncodedImageFormat.Png);

        // The IHDR chunk's width and height are big-endian at offsets 16 and 20,
        // and its CRC over the type and data (offsets 12 to 28) follows at 29.
        WriteBigEndian(png, 16, (uint)width);
        WriteBigEndian(png, 20, (uint)height);
        WriteBigEndian(png, 29, Crc32(png, 12, 17));
        return png;
    }

    private static (byte[] Bgra, int Width, int Height) RenderBgra(string text)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = Size, Height = Size, Margin = 4 },
        };
        ZXing.Rendering.PixelData data = writer.Write(text);
        return (data.Pixels, data.Width, data.Height);
    }

    private static byte[] Encode(byte[] bgra, int width, int height, SKEncodedImageFormat format)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        GCHandle pinned = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            using SKImage image = SKImage.FromPixelCopy(info, pinned.AddrOfPinnedObject(), info.RowBytes);
            using SKData encoded = image.Encode(format, 90);
            return encoded.ToArray();
        }
        finally
        {
            pinned.Free();
        }
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] buffer, int offset, int count)
    {
        uint crc = 0xFFFFFFFF;
        for (int index = offset; index < offset + count; index++)
        {
            crc ^= buffer[index];
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }
}
