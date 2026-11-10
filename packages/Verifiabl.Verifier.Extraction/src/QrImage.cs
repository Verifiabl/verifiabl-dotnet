namespace Verifiabl.Verifier.Extraction;

/// <summary>An image's pixels, for reading its Verifiabl QR code.</summary>
/// <remarks>
/// Decode the image file with your own imaging library, then pass its pixels
/// here, tightly packed with no row padding. The SDK keeps a reference to the
/// array, so do not change it while it is being read.
/// </remarks>
public sealed class QrImage
{
    /// <summary>The largest image the SDK reads: 50 megapixels, more than an A4 page scanned at 600 dpi.</summary>
    public const int MaxPixels = 50_000_000;

    private QrImage(byte[] pixels, int width, int height, QrPixelFormat format)
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

    internal QrPixelFormat Format { get; }

    /// <summary>Pixels with 4 bytes each, in red, green, blue, alpha order.</summary>
    /// <exception cref="ArgumentException">The size is out of range or does not match the buffer.</exception>
    public static QrImage FromRgba32(byte[] pixels, int width, int height) =>
        Create(pixels, width, height, QrPixelFormat.Rgba32);

    /// <summary>Pixels with 4 bytes each, in blue, green, red, alpha order, as Windows and Skia often store them.</summary>
    /// <exception cref="ArgumentException">The size is out of range or does not match the buffer.</exception>
    public static QrImage FromBgra32(byte[] pixels, int width, int height) =>
        Create(pixels, width, height, QrPixelFormat.Bgra32);

    /// <summary>Greyscale pixels with 1 byte each.</summary>
    /// <exception cref="ArgumentException">The size is out of range or does not match the buffer.</exception>
    public static QrImage FromGray8(byte[] pixels, int width, int height) =>
        Create(pixels, width, height, QrPixelFormat.Gray8);

    private static QrImage Create(byte[] pixels, int width, int height, QrPixelFormat format)
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

    private static int BytesPerPixel(QrPixelFormat format) => format == QrPixelFormat.Gray8 ? 1 : 4;
}

/// <summary>How a <see cref="QrImage"/> stores its pixels.</summary>
internal enum QrPixelFormat
{
    Rgba32,
    Bgra32,
    Gray8,
}
