using Verifiabl.Verifier.Extraction.Internal;

namespace Verifiabl.Verifier.Extraction;

/// <summary>An image's pixels, for reading its Verifiabl QR code with <see cref="QrPayloadReader"/>.</summary>
/// <remarks>
/// Decode the image file with your own imaging library, then pass its pixels
/// here: rows from top to bottom, tightly packed with no row padding. Alpha is
/// not premultiplied; transparent pixels read as white. The SDK keeps a
/// reference to the array, so do not change it while it is being read.
/// </remarks>
public sealed class QrImage
{
    /// <summary>The largest image the SDK reads: 50 megapixels, more than an A4 page scanned at 600 dpi.</summary>
    public const int MaxPixels = 50_000_000;

    private QrImage(byte[] pixels, int width, int height, PixelFormat format)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        Format = format;
    }

    /// <summary>The image's width in pixels.</summary>
    public int Width { get; }

    /// <summary>The image's height in pixels.</summary>
    public int Height { get; }

    internal byte[] Pixels { get; }

    internal PixelFormat Format { get; }

    /// <summary>Pixels with 4 bytes each, in red, green, blue, alpha order.</summary>
    /// <exception cref="ArgumentException">The size is out of range or does not match the buffer.</exception>
    public static QrImage FromRgba32(byte[] pixels, int width, int height) =>
        Create(pixels, width, height, PixelFormat.Rgba32);

    /// <summary>Pixels with 4 bytes each, in blue, green, red, alpha order, as Windows and Skia often store them.</summary>
    /// <exception cref="ArgumentException">The size is out of range or does not match the buffer.</exception>
    public static QrImage FromBgra32(byte[] pixels, int width, int height) =>
        Create(pixels, width, height, PixelFormat.Bgra32);

    /// <summary>Greyscale pixels with 1 byte each.</summary>
    /// <exception cref="ArgumentException">The size is out of range or does not match the buffer.</exception>
    public static QrImage FromGray8(byte[] pixels, int width, int height) =>
        Create(pixels, width, height, PixelFormat.Gray8);

    private static QrImage Create(byte[] pixels, int width, int height, PixelFormat format)
    {
        if (pixels is null)
        {
            throw new ArgumentNullException(nameof(pixels));
        }

        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
        {
            throw new ArgumentException(
                $"The image must be between 1 and {MaxPixels} pixels.",
                width <= 0 ? nameof(width) : nameof(height));
        }

        long expected = (long)width * height * BytesPerPixel(format);
        if (pixels.LongLength != expected)
        {
            throw new ArgumentException(
                $"Expected {expected} bytes for a {width}x{height} image, got {pixels.LongLength}.",
                nameof(pixels));
        }

        return new QrImage(pixels, width, height, format);
    }

    private static int BytesPerPixel(PixelFormat format) => format == PixelFormat.Gray8 ? 1 : 4;
}
