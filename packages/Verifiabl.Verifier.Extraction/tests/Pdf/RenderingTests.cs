using System.Text;
using Xunit;
using ZXing;
using ZXing.QrCode;

namespace Verifiabl.Verifier.Extraction.Pdf.Tests;

/// <summary>Pages rendered with PDFium in the sandbox, when a PDF has no metadata payload.</summary>
public class RenderingTests
{
    /// <summary>More bytes than the sandbox's 512 MiB memory may hold, from a few kilobytes of Flate data.</summary>
    private const long ExpandsPastTheMemoryCap = 800L * 1024 * 1024;

    [Fact]
    public void FindsAQrCodeDrawnAsVectors()
    {
        string content = TestPdfs.IssuerBadge().Content;

        PdfPayloads payloads = PayslipPdfReader.Read(VectorQrPdf(content));

        Assert.Equal(content, payloads.QrCode);
        Assert.Equal(1, payloads.RenderedPages);
    }

    [Fact]
    public void SurvivesPdfsWithCyclicStructures()
    {
        // Each ended the process in PdfPig 0.1.16 with a stack overflow.
        foreach (byte[] pdf in new[] { TestPdfs.SelfReferencingFilterPdf(), TestPdfs.CyclicDestsPdf() })
        {
            PdfPayloads payloads = PayslipPdfReader.Read(pdf);
            Assert.Null(payloads.Metadata);
            Assert.Null(payloads.QrCode);
        }
    }

    [Fact]
    public void RefusesOrSkipsAPageThatExpandsPastTheMemoryCap()
    {
        byte[] pdf = TestPdfs.FlateContentPdf(ExpandsPastTheMemoryCap);

        try
        {
            Assert.Null(PayslipPdfReader.Read(pdf).QrCode);
        }
        catch (PayloadExtractionException failure)
        {
            Assert.Contains(failure.Failure, new[] { PayloadExtractionFailure.LimitExceeded, PayloadExtractionFailure.Unreadable });
        }
    }

    /// <summary>A one-page PDF whose QR code is filled rectangles in the page content, as a natively placed SVG badge is.</summary>
    private static byte[] VectorQrPdf(string content)
    {
        ZXing.Common.BitMatrix modules = new QRCodeWriter().encode(content, BarcodeFormat.QR_CODE, 1, 1);
        const double ModuleSize = 3;
        var stream = new StringBuilder("0 g\n");
        for (int y = 0; y < modules.Height; y++)
        {
            for (int x = 0; x < modules.Width; x++)
            {
                if (modules[x, y])
                {
                    // PDF's origin is bottom-left, so rows count down from the top.
                    double left = 300 + (x * ModuleSize);
                    double bottom = 700 - (y * ModuleSize);
                    stream.Append(FormattableString.Invariant($"{left} {bottom} {ModuleSize} {ModuleSize} re\n"));
                }
            }
        }

        stream.Append("f\n");
        return TestPdfs.ContentPdf(Encoding.ASCII.GetBytes(stream.ToString()), "[]");
    }
}
