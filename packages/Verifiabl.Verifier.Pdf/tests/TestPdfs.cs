using System.IO.Compression;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace Verifiabl.Verifier.Pdf.Tests;

/// <summary>Builds payslip-like PDFs with chosen images and metadata for the reader tests.</summary>
internal static class TestPdfs
{
    internal const string Reference = "n5waC35dPCoRtMXW5_gJqw";

    /// <summary>Latin-1 maps every byte to one character, on every target framework.</summary>
    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    /// <summary>A real issuer badge PNG and the scan link it encodes.</summary>
    internal static (byte[] Png, string Content) IssuerBadge(byte fill = 7)
    {
        BarcodePngResult badge = VerifiablBarcode.CreatePng(Parts(fill));
        return (badge.Png, badge.Content);
    }

    /// <summary>An issuer badge PNG whose scan link uses the issuer's own scan origin.</summary>
    internal static (byte[] Png, string Content) IssuerBadgeWithScanOrigin(Uri scanBaseUrl, byte fill = 7)
    {
        BarcodePngResult badge = VerifiablBarcode.CreatePng(Parts(fill), new BarcodeSvgOptions { ScanBaseUrl = scanBaseUrl });
        return (badge.Png, badge.Content);
    }

    /// <summary>The bare payload the issuer SDK writes to PDF metadata for the same parts.</summary>
    internal static string IssuerMetadataPayload(byte fill = 7) => VerifiablBarcode.BuildPayload(Parts(fill));

    /// <summary>Rewrite part of a PDF in place. The replacement keeps the length, so offsets stay valid.</summary>
    internal static byte[] Patch(byte[] pdf, string from, string to)
    {
        if (from.Length != to.Length)
        {
            throw new ArgumentException("A patch must keep the length.");
        }

        string text = Latin1.GetString(pdf);
        if (!text.Contains(from))
        {
            throw new ArgumentException($"The PDF does not contain {from}.");
        }

        return Latin1.GetBytes(text.Replace(from, to));
    }

    /// <summary>A one-page PDF with the given PNG and JPEG images and optional XMP payload.</summary>
    internal static byte[] Build(IEnumerable<byte[]>? pngs = null, IEnumerable<byte[]>? jpegs = null, string? metadataPayload = null)
    {
        using var builder = new PdfDocumentBuilder();
        PdfPageBuilder page = builder.AddPage(595, 842);
        double top = 800;
        foreach (byte[] png in pngs ?? [])
        {
            page.AddPng(png, new PdfRectangle(40, top - 150, 190, top));
            top -= 170;
        }

        foreach (byte[] jpeg in jpegs ?? [])
        {
            page.AddJpeg(jpeg, new PdfRectangle(40, top - 150, 190, top));
            top -= 170;
        }

        byte[] pdf = builder.Build();
        return metadataPayload is null ? pdf : WithMetadata(pdf, metadataPayload);
    }

    /// <summary>A PDF with the given number of blank pages and optional XMP payload.</summary>
    internal static byte[] BlankPages(int count, string? metadataPayload = null)
    {
        using var builder = new PdfDocumentBuilder();
        for (int page = 0; page < count; page++)
        {
            builder.AddPage(595, 842);
        }

        byte[] pdf = builder.Build();
        return metadataPayload is null ? pdf : WithMetadata(pdf, metadataPayload);
    }

    /// <summary>
    /// A one-page PDF whose only image stream holds <paramref name="data"/>
    /// ASCII85-encoded, with the filter chain <c>[/ASCII85Decode /DCTDecode]</c>.
    /// </summary>
    internal static byte[] Ascii85Jpeg(byte[] data, int width, int height) =>
        HandBuiltImagePdf(Ascii85(data), "[/ASCII85Decode /DCTDecode]", width, height);

    /// <summary>
    /// A one-page PDF with one image stream and a hand-written <c>/Filter</c>
    /// entry. PdfPig's builder writes only direct single filters, so filter
    /// chains and indirect filters are written by hand. Objects in
    /// <paramref name="extraObjects"/> are numbered from 6.
    /// </summary>
    internal static byte[] HandBuiltImagePdf(
        string stream,
        string filter,
        int width,
        int height,
        params string[] extraObjects)
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /XObject << /Im1 4 0 R >> >> /Contents 5 0 R >>",
            $"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /BitsPerComponent 8 /ColorSpace /DeviceRGB "
                + $"/Filter {filter} /Length {stream.Length} >>\nstream\n{stream}\nendstream",
            "<< /Length 35 >>\nstream\nq 150 0 0 150 40 600 cm /Im1 Do Q\nendstream",
            .. extraObjects,
        ];

        return Assemble(objects);
    }

    /// <summary>A one-page PDF whose image <c>/Filter</c> is an object that refers to itself.</summary>
    internal static byte[] SelfReferencingFilterPdf() =>
        HandBuiltImagePdf("x", "6 0 R", 1, 1, "6 0 R");

    /// <summary>A one-page PDF whose <c>/Dests</c> name tree lists itself as its own kid.</summary>
    internal static byte[] CyclicDestsPdf() =>
        Assemble(
        [
            "<< /Type /Catalog /Pages 2 0 R /Names << /Dests 4 0 R >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>",
            "<< /Kids [4 0 R] >>",
        ]);

    /// <summary>A one-page PDF whose page content is one stream with the given filter entry.</summary>
    internal static byte[] ContentPdf(byte[] stream, string filter)
    {
        string text = RawStream(stream);
        return Assemble(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R >>",
            $"<< /Filter {filter} /Length {text.Length} >>\nstream\n{text}\nendstream",
        ]);
    }

    /// <summary>A one-page PDF whose page content is Flate streams of the given numbers of zero bytes.</summary>
    internal static byte[] FlateContentPdf(params long[] zeros)
    {
        string contents = string.Join(" ", zeros.Select((_, index) => $"{index + 4} 0 R"));
        string[] streams = zeros
            .Select(count => RawStream(ZlibZeros(count)))
            .Select(stream => $"<< /Filter /FlateDecode /Length {stream.Length} >>\nstream\n{stream}\nendstream")
            .ToArray();
        return Assemble(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents [{contents}] >>",
            .. streams,
        ]);
    }

    /// <summary>
    /// Zlib data for <paramref name="count"/> zero bytes, compressed as it is
    /// written, so a test never holds the expanded bytes.
    /// </summary>
    internal static byte[] ZlibZeros(long count) => ZlibRepeated(0, count);

    /// <summary>
    /// Zlib data for <paramref name="count"/> copies of <paramref name="value"/>,
    /// compressed as it is written.
    /// </summary>
    internal static byte[] ZlibRepeated(byte value, long count)
    {
        using var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0x9C);
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            byte[] chunk = Enumerable.Repeat(value, 1 << 20).ToArray();
            for (long written = 0; written < count; written += chunk.Length)
            {
                deflate.Write(chunk, 0, (int)Math.Min(chunk.Length, count - written));
            }
        }

        // Adler-32 of one repeated byte, in closed form: after n bytes the low sum
        // is 1 + n*value and the high sum is n + value*n*(n+1)/2, both mod 65521.
        const ulong Modulus = 65521;
        ulong n = (ulong)count % Modulus;
        ulong low = (1 + (n * value)) % Modulus;
        ulong pairs = (ulong)count % (2 * Modulus) * (((ulong)count + 1) % (2 * Modulus)) / 2 % Modulus;
        ulong high = (n + (value * pairs)) % Modulus;
        uint adler = (uint)((high << 16) | low);
        output.Write([(byte)(adler >> 24), (byte)(adler >> 16), (byte)(adler >> 8), (byte)adler], 0, 4);
        return output.ToArray();
    }

    private static byte[] Assemble(string[] objects)
    {
        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        int xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            pdf.Append($"{offset:D10} 00000 n \n");
        }

        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Latin1.GetBytes(pdf.ToString());
    }

    /// <summary>Bytes as a Latin-1 string, so they can be written into a hand-built PDF unchanged.</summary>
    internal static string RawStream(byte[] data) => Latin1.GetString(data);

    /// <summary>ASCII base-85 encoding, as the PDF ASCII85Decode filter reads it.</summary>
    internal static string Ascii85(byte[] data) => Ascii85Encode(data);

    private static string Ascii85Encode(byte[] data)
    {
        var text = new StringBuilder();
        for (int start = 0; start < data.Length; start += 4)
        {
            int count = Math.Min(4, data.Length - start);
            uint value = 0;
            for (int index = 0; index < 4; index++)
            {
                value = (value << 8) | (index < count ? data[start + index] : 0u);
            }

            char[] digits = new char[5];
            for (int index = 4; index >= 0; index--)
            {
                digits[index] = (char)('!' + (value % 85));
                value /= 85;
            }

            text.Append(digits, 0, count + 1);
        }

        return text.Append("~>").ToString();
    }

    private static BarcodeParts Parts(byte fill) => new(Reference, Enumerable.Repeat(fill, 64).ToArray());

    /// <summary>Append an XMP metadata stream, as an issuer's PDF tool writes the payload copy.</summary>
    private static byte[] WithMetadata(byte[] pdf, string payload)
    {
        string xmp = $"""
            <x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about="" xmlns:verifiabl="https://verifiabl.io/ns/"><verifiabl:payload>{payload}</verifiabl:payload></rdf:Description>
            </rdf:RDF></x:xmpmeta>
            """;
        byte[] data = Encoding.UTF8.GetBytes(xmp);
        byte[] header = Encoding.ASCII.GetBytes($"\n900 0 obj\n<< /Type /Metadata /Subtype /XML /Length {data.Length} >>\nstream\n");
        byte[] footer = Encoding.ASCII.GetBytes("\nendstream\nendobj\n");
        return [.. pdf, .. header, .. data, .. footer];
    }
}
