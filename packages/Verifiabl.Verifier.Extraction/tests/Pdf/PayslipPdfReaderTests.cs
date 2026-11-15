using System.Text;
using Xunit;

namespace Verifiabl.Verifier.Extraction.Pdf.Tests;

public class PayslipPdfReaderTests
{
    [Fact]
    public void ReadsAnIssuerBadgeEmbeddedAsPng()
    {
        (byte[] png, string content) = TestPdfs.IssuerBadge();

        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.Build(pngs: [png]));

        Assert.Equal(content, payloads.QrCode);
        Assert.Null(payloads.Metadata);
    }

    [Fact]
    public void ReturnsTheMetadataWhenTheQrCodeHoldsAnotherPayload()
    {
        (byte[] png, _) = TestPdfs.IssuerBadge(fill: 7);
        (_, string other) = TestPdfs.IssuerBadge(fill: 9);

        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.Build(pngs: [png], metadataPayload: other));

        Assert.Equal(other, payloads.Metadata);
        Assert.Null(payloads.QrCode);
        Assert.DoesNotContain(other, payloads.ToString());
    }

    [Fact]
    public void ReadsTheMetadataOfALongPdfWithoutCheckingItsPages()
    {
        // The page limit bounds rendering, which a metadata payload makes unnecessary.
        byte[] pdf = TestPdfs.BlankPages(PayslipPdfReader.MaxPages + 1, metadataPayload: TestPdfs.IssuerMetadataPayload());

        Assert.NotNull(PayslipPdfReader.Read(pdf).Metadata);
    }

    [Fact]
    public void ReadsABadgeWithTheIssuersOwnScanOrigin()
    {
        (byte[] png, string content) = TestPdfs.IssuerBadgeWithScanOrigin(new Uri("https://verify.example.com"));

        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.Build(pngs: [png]));

        Assert.StartsWith("https://verify.example.com/v/", content);
        Assert.Equal(content, payloads.QrCode);
    }

    [Fact]
    public void ReadsALegacyScanLink()
    {
        const string legacy = "https://v.verifiabl.io/v/1%7C" + TestPdfs.Reference + "%7CYWJj";

        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.Build(pngs: [TestImages.QrPng(legacy)]));

        Assert.Equal(legacy, payloads.QrCode);
    }

    [Fact]
    public void RejectsPdfsOverThePageLimit()
    {
        PayloadExtractionException error = Assert.Throws<PayloadExtractionException>(
            () => PayslipPdfReader.Read(TestPdfs.BlankPages(PayslipPdfReader.MaxPages + 1)));

        Assert.Equal(PayloadExtractionFailure.LimitExceeded, error.Failure);
    }

    [Fact]
    public void ReadsPdfsAtThePageLimit()
    {
        Assert.Null(PayslipPdfReader.Read(TestPdfs.BlankPages(PayslipPdfReader.MaxPages)).QrCode);
    }

    [Fact]
    public void IgnoresOtherQrCodesAndLogos()
    {
        (byte[] badge, string content) = TestPdfs.IssuerBadge();
        byte[] otherCode = TestImages.QrPng("https://example.com/careers");
        byte[] logo = TestImages.QrPng("x", size: 16);

        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.Build(pngs: [logo, otherCode, badge]));

        Assert.Equal(content, payloads.QrCode);
    }

    [Fact]
    public void RejectsTwoDifferentVerifiablCodes()
    {
        (byte[] first, _) = TestPdfs.IssuerBadge(fill: 7);
        (byte[] second, _) = TestPdfs.IssuerBadge(fill: 9);

        PayloadExtractionException error = Assert.Throws<PayloadExtractionException>(
            () => PayslipPdfReader.Read(TestPdfs.Build(pngs: [first, second])));

        Assert.Equal(PayloadExtractionFailure.Ambiguous, error.Failure);
    }

    [Fact]
    public void CountsAScanLinkAndItsBarePayloadInTwoImagesAsOneCode()
    {
        (byte[] badge, string scanLink) = TestPdfs.IssuerBadge();
        byte[] bare = TestImages.QrPng(TestPdfs.IssuerMetadataPayload());

        Assert.Equal(scanLink, PayslipPdfReader.Read(TestPdfs.Build(pngs: [badge, bare])).QrCode);
    }

    [Fact]
    public void RejectsPdfsOverTheSizeLimit()
    {
        PayloadExtractionException error = Assert.Throws<PayloadExtractionException>(
            () => PayslipPdfReader.Read(new byte[PayslipPdfReader.MaxPdfBytes + 1]));

        Assert.Equal(PayloadExtractionFailure.LimitExceeded, error.Failure);
    }

    [Fact]
    public async Task RejectsOversizedStreamsBeforeBufferingThemInFull()
    {
        using var stream = new MemoryStream(new byte[PayslipPdfReader.MaxPdfBytes + 1]);

        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => PayslipPdfReader.ReadAsync(stream));

        Assert.Equal(PayloadExtractionFailure.LimitExceeded, error.Failure);
    }

    [Fact]
    public void ReadingCanBeCancelled()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => PayslipPdfReader.Read(TestPdfs.Build(pngs: [TestPdfs.IssuerBadge().Png]), cancellationToken: cancelled.Token));
    }

    [Fact]
    public async Task ReadAsyncReadsTheMetadataFirst()
    {
        (byte[] png, _) = TestPdfs.IssuerBadge();
        string bare = TestPdfs.IssuerMetadataPayload();
        using var stream = new MemoryStream(TestPdfs.Build(pngs: [png], metadataPayload: bare));

        PdfPayloads payloads = await PayslipPdfReader.ReadAsync(stream);

        Assert.Equal(bare, payloads.Metadata);
        Assert.Null(payloads.QrCode);
    }

    [Fact]
    public async Task AStalledStreamTimesOut()
    {
        var options = new PdfReadOptions { Timeout = TimeSpan.FromMilliseconds(100) };

        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => PayslipPdfReader.ReadAsync(new TestStreams.Stalled(), options));

        Assert.Equal(PayloadExtractionFailure.TimedOut, error.Failure);
    }

    [Fact]
    public async Task WaitsForAStreamThatIgnoresCancellationBeforeReturning()
    {
        // The caller owns the stream, so the SDK must not still be reading it after the call ends.
        var stream = new TestStreams.IgnoresCancellation(TimeSpan.FromMilliseconds(300));
        var options = new PdfReadOptions { Timeout = TimeSpan.FromMilliseconds(50) };

        await Assert.ThrowsAsync<PayloadExtractionException>(() => PayslipPdfReader.ReadAsync(stream, options));

        Assert.True(stream.ReadFinished);
    }

    [Fact]
    public async Task StartsNoReadAfterTheDeadline()
    {
        var stream = new TestStreams.LateChunkThenStalls();
        var options = new PdfReadOptions { Timeout = TimeSpan.FromMilliseconds(50) };

        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => PayslipPdfReader.ReadAsync(stream, options));

        Assert.Equal(PayloadExtractionFailure.TimedOut, error.Failure);
        Assert.Equal(1, stream.Reads);
    }

    [Fact]
    public async Task RejectsAnOversizedArrayBeforeCopyingIt()
    {
        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => PayslipPdfReader.ReadAsync(new byte[PayslipPdfReader.MaxPdfBytes + 1]));

        Assert.Equal(PayloadExtractionFailure.LimitExceeded, error.Failure);
    }

    [Fact]
    public async Task AStreamsOwnCancellationIsNotATimeout()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PayslipPdfReader.ReadAsync(new TestStreams.CancelsItself()));
    }

    [Fact]
    public async Task CallerCancellationIsNotATimeout()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PayslipPdfReader.ReadAsync(TestPdfs.Build(), cancellationToken: cancelled.Token));
    }

    [Fact]
    public async Task RejectsANonPositiveTimeout()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => PayslipPdfReader.ReadAsync(TestPdfs.Build(), new PdfReadOptions { Timeout = TimeSpan.Zero }));
    }

    [Fact]
    public void ReadsTheMetadataWithoutRenderingPages()
    {
        string bare = TestPdfs.IssuerMetadataPayload();

        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.Build(pngs: [TestPdfs.IssuerBadge().Png], metadataPayload: bare));

        Assert.Equal(bare, payloads.Metadata);
        Assert.Null(payloads.QrCode);
        Assert.Equal(0, payloads.RenderedPages);
    }

    [Fact]
    public void ReadsTheQrCodeWhenTheOnlyMetadataValueIsTooLongToBeAPayload()
    {
        (byte[] png, string content) = TestPdfs.IssuerBadge();
        string tooLong = new('A', VerifierClient.MaxBarcodeLength + 1);

        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.Build(pngs: [png], metadataPayload: tooLong));

        Assert.Null(payloads.Metadata);
        Assert.Equal(content, payloads.QrCode);
        Assert.Equal(1, payloads.RenderedPages);
    }

    [Fact]
    public void ReadsMetadataOfTheLongestPayloadWithoutRenderingPages()
    {
        string longest = new('A', VerifierClient.MaxBarcodeLength);

        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.Build(pngs: [TestPdfs.IssuerBadge().Png], metadataPayload: longest));

        Assert.Equal(longest, payloads.Metadata);
        Assert.Null(payloads.QrCode);
        Assert.Equal(0, payloads.RenderedPages);
    }

    [Fact]
    public void ReadsAQrCodeInAScannedJpeg()
    {
        string content = TestPdfs.IssuerBadge().Content;

        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.Build(jpegs: [TestImages.QrJpeg(content)]));

        Assert.Equal(content, payloads.QrCode);
        Assert.Equal(1, payloads.RenderedPages);
    }

    [Fact]
    public void ReportsFilesPdfiumCannotOpenAsUnreadable()
    {
        PayloadExtractionException error = Assert.Throws<PayloadExtractionException>(
            () => PayslipPdfReader.Read(Encoding.ASCII.GetBytes("%PDF-1.7\nnot really a pdf")));

        Assert.Equal(PayloadExtractionFailure.Unreadable, error.Failure);
    }

    [Fact]
    public void ReadsTheMetadataOfAFilePdfiumCannotOpen()
    {
        // The metadata scan reads bytes, not the PDF's structure, so a damaged PDF keeps its payload.
        byte[] pdf = [.. Encoding.ASCII.GetBytes("%PDF-1.7\nnot really a pdf\n"), .. TestPdf.Uncompressed(TestPdf.ElementXmp())];

        Assert.Equal(TestPdf.Payload, PayslipPdfReader.Read(pdf).Metadata);
    }

    [Fact]
    public void PayloadsAreSafeToLog()
    {
        string bare = TestPdfs.IssuerMetadataPayload();

        Assert.DoesNotContain(bare, new PdfPayloads(bare, null).ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfPayloads(null, null, renderedPages: -1));
    }
}
