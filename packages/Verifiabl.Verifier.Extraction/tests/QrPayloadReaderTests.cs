using Xunit;

namespace Verifiabl.Verifier.Extraction.Tests;

public class QrPayloadReaderTests
{
    private const string BarePayload = "2|n5waC35dPCoRtMXW5_gJqw|JVSWY3DPO5XXE3DE";
    private const string OtherBarePayload = "2|AAAAAAAAAAAAAAAAAAAAAA|JVSWY3DPO5XXE3DE";

    [Fact]
    public void ReadsRgbaPixels()
    {
        Assert.Equal(TestQr.ScanLink, QrPayloadReader.Read(TestQr.Render(TestQr.ScanLink).ToImage()));
    }

    [Fact]
    public void ReadsBgraPixels()
    {
        TestQr.Pixels pixels = TestQr.Render(TestQr.ScanLink);

        QrImage image = QrImage.FromBgra32(TestQr.ToBgra(pixels), pixels.Width, pixels.Height);

        Assert.Equal(TestQr.ScanLink, QrPayloadReader.Read(image));
    }

    [Fact]
    public void ReadsGreyscalePixels()
    {
        TestQr.Pixels pixels = TestQr.Render(BarePayload);

        QrImage image = QrImage.FromGray8(TestQr.ToGray(pixels), pixels.Width, pixels.Height);

        Assert.Equal(BarePayload, QrPayloadReader.Read(image));
    }

    [Fact]
    public void ReadsInvertedCodes()
    {
        TestQr.Pixels pixels = TestQr.Render(TestQr.ScanLink);

        QrImage image = QrImage.FromGray8(TestQr.ToGray(pixels, invert: true), pixels.Width, pixels.Height);

        Assert.Equal(TestQr.ScanLink, QrPayloadReader.Read(image));
    }

    [Fact]
    public void IgnoresOtherQrCodes()
    {
        Assert.Null(QrPayloadReader.Read(TestQr.Render("https://example.com/v/n5waC35dPCoRtMXW5_gJqw").ToImage()));
    }

    [Fact]
    public void FindsTheVerifiablCodeBesideAnotherCode()
    {
        TestQr.Pixels page = TestQr.SideBySide(TestQr.Render("https://example.com/careers"), TestQr.Render(TestQr.ScanLink));

        Assert.Equal(TestQr.ScanLink, QrPayloadReader.Read(page.ToImage()));
    }

    [Fact]
    public void CountsAScanLinkAndItsBarePayloadAsOneCode()
    {
        TestQr.Pixels page = TestQr.SideBySide(TestQr.Render(TestQr.ScanLink), TestQr.Render(BarePayload));

        Assert.NotNull(QrPayloadReader.Read(page.ToImage()));
    }

    [Fact]
    public void RejectsTwoDifferentVerifiablCodes()
    {
        TestQr.Pixels page = TestQr.SideBySide(TestQr.Render(TestQr.ScanLink), TestQr.Render(OtherBarePayload));

        PayloadExtractionException error = Assert.Throws<PayloadExtractionException>(() => QrPayloadReader.Read(page.ToImage()));

        Assert.Equal(PayloadExtractionFailure.Ambiguous, error.Failure);
        Assert.DoesNotContain("JVSWY3DP", error.Message);
    }

    [Fact]
    public void RejectsTwoDifferentCodesOfOppositePolarity()
    {
        TestQr.Pixels dark = TestQr.Render(TestQr.ScanLink);
        TestQr.Pixels light = TestQr.Invert(TestQr.Render(OtherBarePayload));
        TestQr.Pixels page = TestQr.SideBySide(dark, light);

        PayloadExtractionException error = Assert.Throws<PayloadExtractionException>(() => QrPayloadReader.Read(page.ToImage()));

        Assert.Equal(PayloadExtractionFailure.Ambiguous, error.Failure);
    }

    [Fact]
    public void ReturnsNullForAnImageWithoutACode()
    {
        byte[] blank = Enumerable.Repeat((byte)255, 200 * 200).ToArray();

        Assert.Null(QrPayloadReader.Read(QrImage.FromGray8(blank, 200, 200)));
    }

    [Fact]
    public void ReadingCanBeCancelled()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => QrPayloadReader.Read(TestQr.Render(TestQr.ScanLink).ToImage(), cancelled.Token));
    }

    [Fact]
    public void ValidatesPixelBuffers()
    {
        Assert.Throws<ArgumentNullException>(() => QrImage.FromRgba32(null!, 1, 1));
        Assert.Throws<ArgumentException>(() => QrImage.FromRgba32(new byte[4], 0, 1));
        Assert.Throws<ArgumentException>(() => QrImage.FromRgba32(new byte[3], 1, 1));
        Assert.Throws<ArgumentException>(() => QrImage.FromGray8(new byte[4], 1, 1));
        Assert.Throws<ArgumentException>(() => QrImage.FromGray8(new byte[1], QrImage.MaxPixels, 2));
    }
}
