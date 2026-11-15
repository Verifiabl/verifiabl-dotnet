using System.Text;
using SkiaSharp;
using Xunit;

namespace Verifiabl.Verifier.Extraction.Images.Tests;

/// <summary>Formats and damaged files, through the sandboxed reader.</summary>
public class ImageFormatTests
{
    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void ReadsTheQrCodeInEachFormat(SKEncodedImageFormat format)
    {
        Assert.Equal(TestImages.ScanLink, PayslipImageReader.Read(TestImages.QrFile(TestImages.ScanLink, format)).QrCode);
    }

    [Fact]
    public void ReadsCodesOnATransparentBackground()
    {
        byte[] badge = TestImages.QrFile(TestImages.ScanLink, SKEncodedImageFormat.Png, transparent: true);

        Assert.Equal(TestImages.ScanLink, PayslipImageReader.Read(badge).QrCode);
    }

    [Fact]
    public void ReportsFilesThatAreNotImagesAsUnreadable()
    {
        AssertFailure(PayloadExtractionFailure.Unreadable, Encoding.ASCII.GetBytes("%PDF-1.7 not an image"));
        AssertFailure(PayloadExtractionFailure.Unreadable, []);
    }

    [Fact]
    public void RefusesFormatsOtherThanPngJpegAndWebp()
    {
        AssertFailure(PayloadExtractionFailure.Unreadable, TestImages.Gif);
    }

    [Fact]
    public void ChecksTheHeadersPixelCountBeforeDecoding()
    {
        AssertFailure(PayloadExtractionFailure.LimitExceeded, TestImages.PngClaiming(20_000, 20_000));
    }

    [Fact]
    public void RefusesFilesOverTheSizeLimit()
    {
        AssertFailure(PayloadExtractionFailure.LimitExceeded, new byte[PayslipImageReader.MaxFileBytes + 1]);
    }

    [Fact]
    public void ReportsDamagedImagesAsUnreadable()
    {
        byte[] png = TestImages.QrFile(TestImages.ScanLink, SKEncodedImageFormat.Png);
        byte[] damaged = png.Take(64).Concat(Enumerable.Repeat((byte)0xA5, png.Length - 64)).ToArray();

        AssertFailure(PayloadExtractionFailure.Unreadable, damaged);
    }

    [Fact]
    public void RejectsANullFile()
    {
        Assert.Throws<ArgumentNullException>(() => PayslipImageReader.Read(null!));
    }

    private static void AssertFailure(PayloadExtractionFailure failure, byte[] file)
    {
        PayloadExtractionException error = Assert.Throws<PayloadExtractionException>(() => PayslipImageReader.Read(file));
        Assert.Equal(failure, error.Failure);
    }
}
