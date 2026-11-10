using System.IO.Compression;
using System.Text;

namespace Verifiabl.Verifier.Pdf.Tests;

/// <summary>Writes minimal PDFs with chosen metadata streams for the reader tests.</summary>
internal static class TestPdf
{
    internal const string Payload = "2|n5waC35dPCoRtMXW5_gJqw|JVSWY3DPO5XXE3DE";

    /// <summary>The XMP packet the demo writes with pdf-lib.</summary>
    internal static string ElementXmp(string payload = Payload, string prefix = "verifiabl", string ns = "https://verifiabl.io/ns/") =>
        $"""
        <?xpacket begin="﻿" id="W5M0MpCehiHzreSzNTczkc9d"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about="" xmlns:{prefix}="{ns}">
              <{prefix}:payload>{payload}</{prefix}:payload>
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        <?xpacket end="w"?>
        """;

    internal static string AttributeXmp(string payload = Payload) =>
        $"""
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about="" xmlns:vf="https://verifiabl.io/ns/" vf:payload="{payload}"/>
          </rdf:RDF>
        </x:xmpmeta>
        """;

    /// <summary>A PDF whose catalog and page are followed by the given metadata streams.</summary>
    internal static byte[] With(params byte[][] metadataObjects)
    {
        using var pdf = new MemoryStream();
        Write(pdf, "%PDF-1.7\n%âãÏÓ\n");
        Write(pdf, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Write(pdf, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        Write(pdf, "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>\nendobj\n");
        foreach (byte[] metadata in metadataObjects)
        {
            pdf.Write(metadata, 0, metadata.Length);
        }

        Write(pdf, "trailer\n<< /Root 1 0 R >>\n%%EOF\n");
        return pdf.ToArray();
    }

    internal static byte[] Uncompressed(string xmp, int objectNumber = 10) =>
        Stream(objectNumber, "/Type /Metadata /Subtype /XML", Encoding.UTF8.GetBytes(xmp), directLength: true);

    internal static byte[] Flate(string xmp, int objectNumber = 10) =>
        Stream(objectNumber, "/Type /Metadata /Subtype /XML /Filter /FlateDecode", Zlib(Encoding.UTF8.GetBytes(xmp)), directLength: true);

    internal static byte[] IndirectLength(string xmp, int objectNumber = 10) =>
        Stream(objectNumber, "/Type/Metadata/Subtype/XML", Encoding.UTF8.GetBytes(xmp), directLength: false);

    internal static byte[] Stream(int objectNumber, string dictionary, byte[] data, bool directLength)
    {
        string length = directLength ? data.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) : "99 0 R";
        using var stream = new MemoryStream();
        Write(stream, $"{objectNumber} 0 obj\n<< {dictionary} /Length {length} >>\nstream\r\n");
        stream.Write(data, 0, data.Length);
        Write(stream, "\r\nendstream\nendobj\n");
        return stream.ToArray();
    }

    /// <summary>zlib framing (RFC 1950) around raw deflate, as PDF FlateDecode stores it.</summary>
    internal static byte[] Zlib(byte[] data)
    {
        using var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0x9C);
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        uint adler = Adler32(data);
        output.WriteByte((byte)(adler >> 24));
        output.WriteByte((byte)(adler >> 16));
        output.WriteByte((byte)(adler >> 8));
        output.WriteByte((byte)adler);
        return output.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        const uint Modulus = 65521;
        uint a = 1;
        uint b = 0;
        foreach (byte value in data)
        {
            a = (a + value) % Modulus;
            b = (b + a) % Modulus;
        }

        return (b << 16) | a;
    }

    private static void Write(Stream stream, string text)
    {
        byte[] bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}
