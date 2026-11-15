using SkiaSharp;
using Xunit;

namespace Verifiabl.Verifier.Extraction.Images.Tests;

public class PayslipImageReaderTests
{
    [Fact]
    public async Task ReadsTheQrCodeFromBytesAndStreams()
    {
        byte[] file = TestImages.QrFile(TestImages.ScanLink, SKEncodedImageFormat.Jpeg);
        using var stream = new MemoryStream(file);

        Assert.Equal(TestImages.ScanLink, (await PayslipImageReader.ReadAsync(file)).QrCode);
        Assert.Equal(TestImages.ScanLink, (await PayslipImageReader.ReadAsync(stream)).QrCode);
        Assert.Equal(TestImages.ScanLink, PayslipImageReader.Read(file).QrCode);
    }

    [Fact]
    public async Task ReturnsNoQrCodeForAnImageWithoutOne()
    {
        ImagePayload payload = await PayslipImageReader.ReadAsync(TestImages.BlankPng());

        Assert.Null(payload.QrCode);
    }

    [Fact]
    public async Task RefusesTwoDifferentCodes()
    {
        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => PayslipImageReader.ReadAsync(TestImages.TwoQrPng(TestImages.ScanLink, TestImages.OtherScanLink)));

        Assert.Equal(PayloadExtractionFailure.Ambiguous, error.Failure);
    }

    [Fact]
    public async Task StopsReadingAnOversizedStreamAtTheLimit()
    {
        var stream = new TestStreams.Endless();

        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => PayslipImageReader.ReadAsync(stream));

        Assert.Equal(PayloadExtractionFailure.LimitExceeded, error.Failure);
        Assert.InRange(stream.BytesRead, PayslipImageReader.MaxFileBytes, PayslipImageReader.MaxFileBytes + (128 * 1024));
    }

    [Fact]
    public async Task RefusesOversizedBytesBeforeCopyingThem()
    {
        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => PayslipImageReader.ReadAsync(new byte[PayslipImageReader.MaxFileBytes + 1]));

        Assert.Equal(PayloadExtractionFailure.LimitExceeded, error.Failure);
    }

    [Fact]
    public async Task AStalledStreamTimesOut()
    {
        var options = new ImageReadOptions { Timeout = TimeSpan.FromMilliseconds(100) };

        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => PayslipImageReader.ReadAsync(new TestStreams.Stalled(), options));

        Assert.Equal(PayloadExtractionFailure.TimedOut, error.Failure);
        Assert.True(error.IsRetryable);
    }

    [Fact]
    public async Task CallerCancellationIsNotATimeout()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PayslipImageReader.ReadAsync(new TestStreams.Stalled(), cancellationToken: cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PayslipImageReader.ReadAsync(TestImages.BlankPng(), cancellationToken: cancelled.Token));
    }

    [Fact]
    public async Task RejectsANonPositiveTimeout()
    {
        var options = new ImageReadOptions { Timeout = TimeSpan.Zero };

        await Assert.ThrowsAsync<ArgumentException>(() => PayslipImageReader.ReadAsync(TestImages.BlankPng(), options));
    }

    [Fact]
    public void PayloadsAreSafeToLog()
    {
        var payload = new ImagePayload(TestImages.ScanLink);

        Assert.Equal("ImagePayload { HasQrCode = True }", payload.ToString());
        Assert.Equal("ImagePayload { HasQrCode = False }", new ImagePayload(null).ToString());
    }
}
