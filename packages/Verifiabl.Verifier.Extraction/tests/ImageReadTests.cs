using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using Verifiabl.Verifier.Extraction.Internal;
using Xunit;

namespace Verifiabl.Verifier.Extraction.Tests;

public class ImageReadTests
{
    private const string ScanLink = "https://v.sandbox.verifiabl.io/v/n5waC35dPCoRtMXW5_gJqw#2.JVSWY3DPO5XXE3DE";

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void ReadsTheQrCodeInEachFormatUnchanged(SKEncodedImageFormat format)
    {
        Assert.Equal([ScanLink], ExtractionModule.ReadImage(QrFile(ScanLink, format), CancellationToken.None));
    }

    [Fact]
    public void ReadsNoCodesFromAnImageWithoutOne()
    {
        Assert.Empty(ExtractionModule.ReadImage(Blank(64, 48), CancellationToken.None));
    }

    [Fact]
    public void ReportsFilesThatAreNotImagesAsUnreadable()
    {
        var failure = Assert.Throws<PayloadExtractionException>(
            () => ExtractionModule.ReadImage(Encoding.ASCII.GetBytes("%PDF-1.7 not an image"), CancellationToken.None));

        Assert.Equal(PayloadExtractionFailure.Unreadable, failure.Failure);
    }

    [Fact]
    public void RefusesAnImageThatNeedsMoreMemoryThanTheCap()
    {
        // A 2000x2000 RGB frame needs 12 MB, more than an 8 MiB cap.
        byte[] png = Blank(2000, 2000);

        var failure = Assert.Throws<PayloadExtractionException>(
            () => ExtractionModule.ReadImage(png, 8L * 1024 * 1024, CancellationToken.None));

        Assert.Equal(PayloadExtractionFailure.LimitExceeded, failure.Failure);
    }

    private static byte[] QrFile(string text, SKEncodedImageFormat format)
    {
        var writer = new ZXing.BarcodeWriterPixelData
        {
            Format = ZXing.BarcodeFormat.QR_CODE,
            Options = new ZXing.Common.EncodingOptions { Width = 300, Height = 300, Margin = 4 },
        };
        ZXing.Rendering.PixelData data = writer.Write(text);
        var info = new SKImageInfo(data.Width, data.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        GCHandle pinned = GCHandle.Alloc(data.Pixels, GCHandleType.Pinned);
        try
        {
            using SKImage image = SKImage.FromPixelCopy(info, pinned.AddrOfPinnedObject(), info.RowBytes);
            using SKData encoded = image.Encode(format, 95);
            return encoded.ToArray();
        }
        finally
        {
            pinned.Free();
        }
    }

    private static byte[] Blank(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        bitmap.Erase(SKColors.White);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
