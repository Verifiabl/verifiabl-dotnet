using System.Diagnostics;
using System.Text;
using Verifiabl.Verifier.Extraction;
using Verifiabl.Verifier.Pdf.Internal;
using Xunit;

namespace Verifiabl.Verifier.Pdf.Tests;

/// <summary>The XMP metadata copy, read without PdfPig.</summary>
public class PdfMetadataReaderTests
{
    [Fact]
    public void ReadsTheUncompressedPacketTheDemoWrites()
    {
        byte[] pdf = TestPdf.With(TestPdf.Uncompressed(TestPdf.ElementXmp()));

        Assert.Equal(TestPdf.Payload, Find(pdf));
    }

    [Fact]
    public void ReadsFlateCompressedMetadata()
    {
        byte[] pdf = TestPdf.With(TestPdf.Flate(TestPdf.ElementXmp()));

        Assert.Equal(TestPdf.Payload, Find(pdf));
    }

    [Fact]
    public void ReadsMetadataWithAnIndirectLength()
    {
        byte[] pdf = TestPdf.With(TestPdf.IndirectLength(TestPdf.ElementXmp()));

        Assert.Equal(TestPdf.Payload, Find(pdf));
    }

    [Fact]
    public void ReadsMetadataWhoseDictionaryNestsAnother()
    {
        byte[] metadata = TestPdf.Stream(
            10,
            "/Type /Metadata /Subtype /XML /Extra << /Nested << /Deep true >> >>",
            Encoding.UTF8.GetBytes(TestPdf.ElementXmp()),
            directLength: true);

        Assert.Equal(TestPdf.Payload, Find(TestPdf.With(metadata)));
    }

    [Fact]
    public void MatchesTheNamespaceWhateverThePrefix()
    {
        byte[] pdf = TestPdf.With(TestPdf.Uncompressed(TestPdf.ElementXmp(prefix: "vf")));

        Assert.Equal(TestPdf.Payload, Find(pdf));
    }

    [Fact]
    public void ReadsTheAttributeForm()
    {
        byte[] pdf = TestPdf.With(TestPdf.Uncompressed(TestPdf.AttributeXmp()));

        Assert.Equal(TestPdf.Payload, Find(pdf));
    }

    [Fact]
    public void IgnoresPayloadPropertiesInOtherNamespaces()
    {
        byte[] pdf = TestPdf.With(TestPdf.Uncompressed(TestPdf.ElementXmp(ns: "https://example.com/ns/")));

        Assert.Null(Find(pdf));
    }

    [Fact]
    public void ReturnsNullWithoutMetadata()
    {
        Assert.Null(Find(TestPdf.With()));
    }

    [Fact]
    public void IgnoresStreamsThatAreNotMetadata()
    {
        byte[] content = TestPdf.Stream(4, "", Encoding.UTF8.GetBytes(TestPdf.ElementXmp()), directLength: true);

        Assert.Null(Find(TestPdf.With(content)));
    }

    [Fact]
    public void AcceptsTheSamePayloadInSeveralStreams()
    {
        byte[] pdf = TestPdf.With(
            TestPdf.Uncompressed(TestPdf.ElementXmp(), objectNumber: 10),
            TestPdf.Flate(TestPdf.ElementXmp(), objectNumber: 11));

        Assert.Equal(TestPdf.Payload, Find(pdf));
    }

    [Fact]
    public void RejectsDifferentPayloads()
    {
        byte[] pdf = TestPdf.With(
            TestPdf.Uncompressed(TestPdf.ElementXmp(), objectNumber: 10),
            TestPdf.Uncompressed(TestPdf.ElementXmp("2|AAAAAAAAAAAAAAAAAAAAAA|JVSWY3DP"), objectNumber: 11));

        PayloadExtractionException error = Assert.Throws<PayloadExtractionException>(() => Find(pdf));

        Assert.Equal(PayloadExtractionFailure.Ambiguous, error.Failure);
        Assert.DoesNotContain(TestPdf.Payload, error.Message);
    }

    [Fact]
    public void DoesNotProcessDtdsOrExternalEntities()
    {
        const string xmp = """
            <?xml version="1.0"?>
            <!DOCTYPE x [<!ENTITY secret SYSTEM "file:///etc/passwd">]>
            <x xmlns:verifiabl="https://verifiabl.io/ns/"><verifiabl:payload>&secret;</verifiabl:payload></x>
            """;

        Assert.Null(Find(TestPdf.With(TestPdf.Uncompressed(xmp))));
    }

    [Fact]
    public void SkipsMetadataThatInflatesPastTheLimit()
    {
        string padded = TestPdf.ElementXmp().Replace("<rdf:RDF", new string(' ', 2 * 1024 * 1024) + "<rdf:RDF");

        Assert.Null(Find(TestPdf.With(TestPdf.Flate(padded))));
    }

    [Fact]
    public void SkipsCorruptFlateData()
    {
        byte[] corrupt = TestPdf.Stream(10, "/Type /Metadata /Filter /FlateDecode", [0x78, 0x9C, 0xFF, 0xFF, 0xFF], directLength: true);

        Assert.Null(Find(TestPdf.With(corrupt)));
    }

    [Fact]
    public void IgnoresValuesTooLongToBeAPayload()
    {
        string tooLong = new('A', VerifierClient.MaxBarcodeLength + 1);

        Assert.Null(Find(TestPdf.With(TestPdf.Uncompressed(TestPdf.ElementXmp(tooLong)))));
    }

    [Fact]
    public void RejectsMoreMetadataStreamsThanAPayslipNeeds()
    {
        byte[][] streams = Enumerable.Range(10, 65).Select(number => TestPdf.Uncompressed(TestPdf.ElementXmp(), number)).ToArray();

        Assert.Equal(
            PayloadExtractionFailure.LimitExceeded,
            Assert.Throws<PayloadExtractionException>(() => Find(TestPdf.With(streams))).Failure);
    }

    [Fact]
    public void RejectsMetadataThatInflatesPastThePdfBudget()
    {
        // Each stream is just under the per-stream limit; together they pass the PDF's budget.
        string padded = TestPdf.ElementXmp().Replace("<rdf:RDF", new string(' ', 1000 * 1024) + "<rdf:RDF");
        byte[][] streams = Enumerable.Range(10, 5).Select(number => TestPdf.Flate(padded, number)).ToArray();

        Assert.Equal(
            PayloadExtractionFailure.LimitExceeded,
            Assert.Throws<PayloadExtractionException>(() => Find(TestPdf.With(streams))).Failure);
    }

    [Theory]
    [InlineData(">>stream\n")]
    [InlineData("1 0 obj<</Type/Metadata/Length 9 0 R>>stream\n")]
    public void ScansHostileInputInLinearTime(string unit)
    {
        var pdf = new StringBuilder(PayslipPdfReader.MaxPdfBytes);
        while (pdf.Length + unit.Length <= PayslipPdfReader.MaxPdfBytes)
        {
            pdf.Append(unit);
        }

        var elapsed = Stopwatch.StartNew();
        Assert.Null(Find(Encoding.ASCII.GetBytes(pdf.ToString())));

        // A quadratic scan of 20 MiB takes hours; linear takes well under a second.
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(20), $"Scan took {elapsed.Elapsed}.");
    }

    [Fact]
    public void ScansLengthsThatPointIntoASharedWhitespaceRunInLinearTime()
    {
        // Each header declares a direct length landing in one long run of spaces with no endstream.
        const string Header = "1 0 obj<</Type/Metadata/Length 1048576>>stream\n";
        var pdf = new StringBuilder(PayslipPdfReader.MaxPdfBytes);
        while (pdf.Length + Header.Length <= 1024 * 1024)
        {
            pdf.Append(Header);
        }

        pdf.Append(' ', PayslipPdfReader.MaxPdfBytes - pdf.Length);

        var elapsed = Stopwatch.StartNew();
        Assert.Null(Find(Encoding.ASCII.GetBytes(pdf.ToString())));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(20), $"Scan took {elapsed.Elapsed}.");
    }

    [Fact]
    public void ReadingCanBeCancelled()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => PdfMetadataReader.FindPayload(TestPdf.With(TestPdf.Flate(TestPdf.ElementXmp())), VerifierClient.MaxBarcodeLength, cancelled.Token));
    }

    [Fact]
    public void IgnoresNonPdfInput()
    {
        Assert.Null(Find(Encoding.UTF8.GetBytes("stream\nendstream")));
        Assert.Null(Find([]));
    }

    [Fact]
    public void AcceptsAScanLinkAndItsBarePayloadInDifferentStreams()
    {
        const string ScanLink = "https://v.verifiabl.io/v/n5waC35dPCoRtMXW5_gJqw#2.JVSWY3DPO5XXE3DE";
        byte[] pdf = TestPdf.With(
            TestPdf.Uncompressed(TestPdf.ElementXmp(), objectNumber: 10),
            TestPdf.Uncompressed(TestPdf.ElementXmp(ScanLink), objectNumber: 11));

        Assert.Equal(TestPdf.Payload, Find(pdf));
    }

    private static string? Find(byte[] pdf) =>
        PdfMetadataReader.FindPayload(pdf, VerifierClient.MaxBarcodeLength, CancellationToken.None);
}
