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
    public void ReadsTransparentPixelsAsWhite()
    {
        TestQr.Pixels pixels = TestQr.Render(TestQr.ScanLink);

        // Light pixels become transparent black, as some badge exports store them.
        byte[] rgba = (byte[])pixels.Rgba.Clone();
        for (int index = 0; index < rgba.Length; index += 4)
        {
            if (rgba[index] > 128)
            {
                rgba[index] = rgba[index + 1] = rgba[index + 2] = rgba[index + 3] = 0;
            }
        }

        Assert.Equal(TestQr.ScanLink, QrPayloadReader.Read(QrImage.FromRgba32(rgba, pixels.Width, pixels.Height)));
    }

    [Fact]
    public async Task ReadsWithinATimeBudget()
    {
        Assert.Equal(TestQr.ScanLink, await QrPayloadReader.ReadAsync(TestQr.Render(TestQr.ScanLink).ToImage()));
    }

    [Fact]
    public async Task AReadPastItsTimeBudgetTimesOut()
    {
        // The stand-in never finishes a read, so only the time budget can end it.
        QrImage image = QrImage.FromGray8(new byte[16 * 16], 16, 16);
        var options = new QrReadOptions { Timeout = TimeSpan.FromMilliseconds(50) };

        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => QrPayloadReader.ReadAsync(image, options, StandInModules.Spins, CancellationToken.None));

        Assert.Equal(PayloadExtractionFailure.TimedOut, error.Failure);
        Assert.True(error.IsRetryable);
    }

    [Fact]
    public async Task CallerCancellationIsNotATimeout()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => QrPayloadReader.ReadAsync(TestQr.Render(TestQr.ScanLink).ToImage(), cancellationToken: cancelled.Token));
    }

    [Fact]
    public async Task RejectsANonPositiveTimeout()
    {
        var options = new QrReadOptions { Timeout = TimeSpan.Zero };

        await Assert.ThrowsAsync<ArgumentException>(
            () => QrPayloadReader.ReadAsync(TestQr.Render(TestQr.ScanLink).ToImage(), options));
    }

    [Fact]
    public async Task RejectsANullImage()
    {
        Assert.Throws<ArgumentNullException>(() => QrPayloadReader.Read(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => QrPayloadReader.ReadAsync(null!));
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

    [Fact]
    public void SelectsThePayloadFromTheTextsRead()
    {
        string? payload = QrPayloadReader.SelectPayload(["https://example.com/menu", TestQr.ScanLink, TestQr.ScanLink]);

        Assert.Equal(TestQr.ScanLink, payload);
    }

    [Fact]
    public void SelectsNothingWhenNoTextIsAPayload()
    {
        Assert.Null(QrPayloadReader.SelectPayload(["https://example.com/menu"]));
    }

    [Fact]
    public void RefusesTwoDifferentPayloadsInTheTextsRead()
    {
        const string other = "https://v.sandbox.verifiabl.io/v/Q7xd2kLm9PqRsTuVwXyZ0a#2.MFRGGZDFMZTWQ2LK";

        var failure = Assert.Throws<PayloadExtractionException>(() => QrPayloadReader.SelectPayload([TestQr.ScanLink, other]));

        Assert.Equal(PayloadExtractionFailure.Ambiguous, failure.Failure);
    }
}
