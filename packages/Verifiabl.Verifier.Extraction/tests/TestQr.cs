using ZXing;
using ZXing.Common;

namespace Verifiabl.Verifier.Extraction.Tests;

/// <summary>Renders QR codes to RGBA pixels for the image reader tests.</summary>
internal static class TestQr
{
    internal const string ScanLink = "https://v.sandbox.verifiabl.io/v/n5waC35dPCoRtMXW5_gJqw#2.JVSWY3DPO5XXE3DE";

    internal sealed class Pixels
    {
        internal Pixels(byte[] rgba, int width, int height)
        {
            Rgba = rgba;
            Width = width;
            Height = height;
        }

        internal byte[] Rgba { get; }

        internal int Width { get; }

        internal int Height { get; }

        internal QrImage ToImage() => QrImage.FromRgba32(Rgba, Width, Height);
    }

    internal static Pixels Render(string text, int size = 300)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = size, Height = size, Margin = 4 },
        };
        ZXing.Rendering.PixelData data = writer.Write(text);

        // ZXing writes BGRA; swap to RGBA so each format is tested explicitly.
        byte[] rgba = (byte[])data.Pixels.Clone();
        for (int index = 0; index < rgba.Length; index += 4)
        {
            (rgba[index], rgba[index + 2]) = (rgba[index + 2], rgba[index]);
        }

        return new Pixels(rgba, data.Width, data.Height);
    }

    /// <summary>Two images side by side on one canvas, as two codes on one page.</summary>
    internal static Pixels SideBySide(Pixels left, Pixels right)
    {
        int width = left.Width + right.Width;
        int height = Math.Max(left.Height, right.Height);
        byte[] canvas = Enumerable.Repeat((byte)255, width * height * 4).ToArray();
        CopyInto(canvas, width, left, 0);
        CopyInto(canvas, width, right, left.Width);
        return new Pixels(canvas, width, height);
    }

    /// <summary>Light modules on a dark background.</summary>
    internal static Pixels Invert(Pixels pixels)
    {
        byte[] rgba = (byte[])pixels.Rgba.Clone();
        for (int index = 0; index < rgba.Length; index += 4)
        {
            rgba[index] = (byte)(255 - rgba[index]);
            rgba[index + 1] = (byte)(255 - rgba[index + 1]);
            rgba[index + 2] = (byte)(255 - rgba[index + 2]);
        }

        return new Pixels(rgba, pixels.Width, pixels.Height);
    }

    internal static byte[] ToBgra(Pixels pixels)
    {
        byte[] bgra = (byte[])pixels.Rgba.Clone();
        for (int index = 0; index < bgra.Length; index += 4)
        {
            (bgra[index], bgra[index + 2]) = (bgra[index + 2], bgra[index]);
        }

        return bgra;
    }

    internal static byte[] ToGray(Pixels pixels, bool invert = false)
    {
        byte[] gray = new byte[pixels.Width * pixels.Height];
        for (int pixel = 0; pixel < gray.Length; pixel++)
        {
            byte value = pixels.Rgba[pixel * 4];
            gray[pixel] = invert ? (byte)(255 - value) : value;
        }

        return gray;
    }

    private static void CopyInto(byte[] canvas, int canvasWidth, Pixels source, int left)
    {
        for (int row = 0; row < source.Height; row++)
        {
            Buffer.BlockCopy(source.Rgba, row * source.Width * 4, canvas, ((row * canvasWidth) + left) * 4, source.Width * 4);
        }
    }
}
