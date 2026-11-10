using System.Text;
using Verifiabl.Verifier.Extraction;
using Xunit;

namespace Verifiabl.Verifier.Pdf.Tests;

public class PayslipPdfReaderTests
{
    private const int JpegSize = 300;

    [Fact]
    public void ReadsAnIssuerBadgeEmbeddedAsPng()
    {
        (byte[] png, string content) = TestPdfs.IssuerBadge();

        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.Build(pngs: [png]));

        Assert.Equal(content, payloads.QrCode);
        Assert.Null(payloads.Metadata);
    }

    [Fact]
    public void ReadsTheMetadataWithoutDecodingImages()
    {
        string bare = TestPdfs.IssuerMetadataPayload();
        byte[] jpeg = TestImages.JpegHeaderOnly(JpegSize, JpegSize);
        var decoder = new RecordingJpegDecoder(TestImages.RenderQr(TestPdfs.IssuerBadge().Content, JpegSize));

        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.Build(jpegs: [jpeg], metadataPayload: bare), decoder.Options);

        Assert.Equal(bare, payloads.Metadata);
        Assert.Null(payloads.QrCode);
        Assert.Equal(0, payloads.SkippedImages);
        Assert.Empty(decoder.Received);
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
        // Page and image limits bound image reading, which a metadata payload makes unnecessary.
        byte[] pdf = TestPdfs.BlankPages(PayslipPdfReader.MaxPages + 1, metadataPayload: TestPdfs.IssuerMetadataPayload());

        Assert.NotNull(PayslipPdfReader.Read(pdf).Metadata);
    }

    [Fact]
    public void SkipsJpegImagesWithoutADecoder()
    {
        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.Build(jpegs: [TestImages.JpegHeaderOnly(JpegSize, JpegSize)]));

        Assert.Null(payloads.QrCode);
        Assert.Equal(1, payloads.SkippedImages);
        Assert.Null(payloads.Metadata);
    }

    [Fact]
    public void PassesJpegBytesToTheDecoder()
    {
        string content = TestPdfs.IssuerBadge().Content;
        byte[] jpeg = TestImages.JpegHeaderOnly(JpegSize, JpegSize);
        var decoder = new RecordingJpegDecoder(TestImages.RenderQr(content, JpegSize));

        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.Build(jpegs: [jpeg]), decoder.Options);

        Assert.Equal(content, payloads.QrCode);
        Assert.Equal(0, payloads.SkippedImages);
        Assert.Equal(jpeg, Assert.Single(decoder.Received));
    }

    [Fact]
    public void ReadsJpegFiltersWrittenAsAnArray()
    {
        string content = TestPdfs.IssuerBadge().Content;
        byte[] pdf = TestPdfs.Patch(
            TestPdfs.Build(jpegs: [TestImages.JpegHeaderOnly(JpegSize, JpegSize)]),
            "/ColorSpace /DeviceRGB /Filter /DCTDecode",
            "/ColorSpace/DeviceRGB /Filter[/DCTDecode]");
        var decoder = new RecordingJpegDecoder(TestImages.RenderQr(content, JpegSize));

        Assert.Equal(content, PayslipPdfReader.Read(pdf, decoder.Options).QrCode);
        Assert.Single(decoder.Received);
    }

    [Fact]
    public void ReadsJpegsWrappedInAnEarlierFilter()
    {
        string content = TestPdfs.IssuerBadge().Content;
        byte[] jpeg = TestImages.JpegHeaderOnly(JpegSize, JpegSize);
        var decoder = new RecordingJpegDecoder(TestImages.RenderQr(content, JpegSize));

        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.Ascii85Jpeg(jpeg, JpegSize, JpegSize), decoder.Options);

        Assert.Equal(content, payloads.QrCode);
        Assert.Equal(jpeg, Assert.Single(decoder.Received));
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
    public void RefusesPageContentThatExpandsPastTheStreamLimit()
    {
        byte[] pdf = TestPdfs.FlateContentPdf(PayslipPdfReader.MaxDecodedStreamBytes + 1);

        PayloadExtractionException error = Assert.Throws<PayloadExtractionException>(() => PayslipPdfReader.Read(pdf));

        Assert.Equal(PayloadExtractionFailure.LimitExceeded, error.Failure);
    }

    [Fact]
    public void RefusesAnImageStreamThatExpandsPastTheStreamLimit()
    {
        string stream = TestPdfs.RawStream(TestPdfs.ZlibZeros(PayslipPdfReader.MaxDecodedStreamBytes + 1));
        byte[] pdf = TestPdfs.HandBuiltImagePdf(stream, "/FlateDecode", 64, 64);

        PayloadExtractionException error = Assert.Throws<PayloadExtractionException>(() => PayslipPdfReader.Read(pdf));

        Assert.Equal(PayloadExtractionFailure.LimitExceeded, error.Failure);
    }

    [Fact]
    public void RefusesStreamsThatTogetherExpandPastThePdfLimit()
    {
        long eachStream = (PayslipPdfReader.MaxDecodedBytes / 2) + 1;
        byte[] pdf = TestPdfs.FlateContentPdf(eachStream, eachStream);

        PayloadExtractionException error = Assert.Throws<PayloadExtractionException>(() => PayslipPdfReader.Read(pdf));

        Assert.Equal(PayloadExtractionFailure.LimitExceeded, error.Failure);
    }

    [Fact]
    public void BoundsAscii85ZeroAbbreviationsBeforeDecodingThem()
    {
        // Each z decodes to four zero bytes, so ASCII85 can expand fourfold.
        long abbreviations = (PayslipPdfReader.MaxDecodedStreamBytes / 4) + 1;
        byte[] stream = TestPdfs.ZlibRepeated((byte)'z', abbreviations);
        byte[] pdf = TestPdfs.ContentPdf(stream, "[/FlateDecode /ASCII85Decode]");

        PayloadExtractionException error = Assert.Throws<PayloadExtractionException>(() => PayslipPdfReader.Read(pdf));

        Assert.Equal(PayloadExtractionFailure.LimitExceeded, error.Failure);
    }

    [Fact]
    public void ReadsCompressedPageContentWithinTheLimit()
    {
        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.FlateContentPdf(1024 * 1024));

        Assert.Null(payloads.QrCode);
    }

    [Fact]
    public void ReadsALegacyScanLink()
    {
        const string legacy = "https://v.verifiabl.io/v/1%7C" + TestPdfs.Reference + "%7CYWJj";

        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.Build(pngs: [TestImages.QrPng(legacy)]));

        Assert.Equal(legacy, payloads.QrCode);
    }

    [Fact]
    public void SkipsJpegsWrappedInADecompressingFilterWithoutExpandingThem()
    {
        byte[] jpeg = TestImages.JpegHeaderOnly(JpegSize, JpegSize);
        var decoder = new RecordingJpegDecoder(TestImages.RenderQr(TestPdfs.IssuerBadge().Content, JpegSize));
        byte[] pdf = TestPdfs.HandBuiltImagePdf(
            TestPdfs.RawStream(TestPdf.Zlib(jpeg)),
            "[/FlateDecode /DCTDecode]",
            JpegSize,
            JpegSize);

        PdfPayloads payloads = PayslipPdfReader.Read(pdf, decoder.Options);

        Assert.Null(payloads.QrCode);
        Assert.Equal(1, payloads.SkippedImages);
        Assert.Empty(decoder.Received);
    }

    [Fact]
    public void ResolvesAnIndirectJpegFilter()
    {
        string content = TestPdfs.IssuerBadge().Content;
        byte[] jpeg = TestImages.JpegHeaderOnly(JpegSize, JpegSize);
        var decoder = new RecordingJpegDecoder(TestImages.RenderQr(content, JpegSize));
        byte[] pdf = TestPdfs.HandBuiltImagePdf(TestPdfs.RawStream(jpeg), "6 0 R", JpegSize, JpegSize, "/DCTDecode");

        Assert.Equal(content, PayslipPdfReader.Read(pdf, decoder.Options).QrCode);
        Assert.Equal(jpeg, Assert.Single(decoder.Received));
    }

    [Fact]
    public void ResolvesAnIndirectFilterArrayAndItsEntries()
    {
        string content = TestPdfs.IssuerBadge().Content;
        byte[] jpeg = TestImages.JpegHeaderOnly(JpegSize, JpegSize);
        var decoder = new RecordingJpegDecoder(TestImages.RenderQr(content, JpegSize));
        byte[] pdf = TestPdfs.HandBuiltImagePdf(
            TestPdfs.Ascii85(jpeg),
            "6 0 R",
            JpegSize,
            JpegSize,
            "[/ASCII85Decode 7 0 R]",
            "/DCTDecode");

        Assert.Equal(content, PayslipPdfReader.Read(pdf, decoder.Options).QrCode);
        Assert.Equal(jpeg, Assert.Single(decoder.Received));
    }

    [Fact]
    public void SkipsJpegsLargerThanTheSizeThePdfDeclaresWithoutDecodingThem()
    {
        string content = TestPdfs.IssuerBadge().Content;
        byte[] pdf = TestPdfs.Patch(
            TestPdfs.Build(jpegs: [TestImages.JpegHeaderOnly(JpegSize, JpegSize)]),
            "/Width 300 /Height 300",
            "/Width 21  /Height 21 ");
        var decoder = new RecordingJpegDecoder(TestImages.RenderQr(content, JpegSize));

        PdfPayloads payloads = PayslipPdfReader.Read(pdf, decoder.Options);

        Assert.Null(payloads.QrCode);
        Assert.Equal(1, payloads.SkippedImages);
        Assert.Empty(decoder.Received);
    }

    [Fact]
    public void SkipsDecodedJpegsWhoseSizeDiffersFromTheHeader()
    {
        string content = TestPdfs.IssuerBadge().Content;
        var decoder = new RecordingJpegDecoder(TestImages.RenderQr(content, 600));

        PdfPayloads payloads = PayslipPdfReader.Read(
            TestPdfs.Build(jpegs: [TestImages.JpegHeaderOnly(JpegSize, JpegSize)]),
            decoder.Options);

        Assert.Null(payloads.QrCode);
        Assert.Equal(1, payloads.SkippedImages);
    }

    [Fact]
    public void SkipsJpegsTheDecoderCannotRead()
    {
        var options = new PdfReadOptions { JpegDecoder = _ => throw new ArgumentException("Not a JPEG.") };

        PdfPayloads payloads = PayslipPdfReader.Read(TestPdfs.Build(jpegs: [TestImages.JpegHeaderOnly(JpegSize, JpegSize)]), options);

        Assert.Equal(1, payloads.SkippedImages);
    }

    [Fact]
    public void SurfacesOtherDecoderFailures()
    {
        var options = new PdfReadOptions { JpegDecoder = _ => throw new InvalidOperationException("Native library missing.") };

        Assert.Throws<InvalidOperationException>(
            () => PayslipPdfReader.Read(TestPdfs.Build(jpegs: [TestImages.JpegHeaderOnly(JpegSize, JpegSize)]), options));
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
    public void RejectsMoreImagesThanAPayslipNeeds()
    {
        byte[] tile = TestImages.QrPng("https://example.com", size: 24);

        PayloadExtractionException error = Assert.Throws<PayloadExtractionException>(
            () => PayslipPdfReader.Read(TestPdfs.Build(pngs: Enumerable.Repeat(tile, PayslipPdfReader.MaxImages + 1))));

        Assert.Equal(PayloadExtractionFailure.LimitExceeded, error.Failure);
    }

    [Fact]
    public void ToleratesFilesPdfPigCannotOpen()
    {
        PdfPayloads payloads = PayslipPdfReader.Read(Encoding.ASCII.GetBytes("%PDF-1.7\nnot really a pdf"));

        Assert.Null(payloads.Metadata);
        Assert.Null(payloads.QrCode);
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
    public async Task ADecodersOwnCancellationIsNotATimeout()
    {
        byte[] pdf = TestPdfs.Build(jpegs: [TestImages.JpegHeaderOnly(JpegSize, JpegSize)]);
        var options = new PdfReadOptions
        {
            JpegDecoder = _ => throw new OperationCanceledException("The decoder was stopped."),
        };

        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PayslipPdfReader.ReadAsync(pdf, options));

        Assert.Equal("The decoder was stopped.", error.Message);
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

    /// <summary>Returns fixed pixels for any JPEG and records what it was given.</summary>
    private sealed class RecordingJpegDecoder
    {
        private readonly TestImages.Gray _pixels;

        internal RecordingJpegDecoder(TestImages.Gray pixels)
        {
            _pixels = pixels;
            Options = new PdfReadOptions { JpegDecoder = Decode };
        }

        internal PdfReadOptions Options { get; }

        internal List<byte[]> Received { get; } = [];

        private QrImage Decode(byte[] jpeg)
        {
            Received.Add(jpeg);
            return _pixels.ToImage();
        }
    }
}
