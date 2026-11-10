using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Verifiabl.Verifier.Extraction;

namespace Verifiabl.Verifier.Pdf.Internal;

/// <summary>
/// Finds the Verifiabl payload in a PDF's XMP metadata streams. It reads only
/// metadata streams, uncompressed or FlateDecode, and never renders the PDF.
/// </summary>
/// <remarks>
/// Issuers write the payload with their own PDF tools, so the property is
/// matched by XML namespace, in element or attribute form, not by prefix.
/// The PDF is untrusted input: the scan is linear in its size, the decoded
/// metadata has a budget for the whole PDF, XML DTDs and external entities are
/// disabled, and an unreadable stream is skipped.
/// </remarks>
internal static class PdfMetadataReader
{
    internal const string PayloadNamespace = "https://verifiabl.io/ns/";
    internal const string PayloadProperty = "payload";

    /// <summary>A metadata stream larger than this, before or after inflating, is skipped.</summary>
    private const int MaxMetadataBytes = 1024 * 1024;

    /// <summary>Decoded metadata for the whole PDF. A payslip has one small packet.</summary>
    private const int MaxTotalMetadataBytes = 4 * 1024 * 1024;

    private const int MaxMetadataStreams = 64;

    /// <summary>How far before a <c>stream</c> keyword its dictionary may start.</summary>
    private const int MaxDictionaryBytes = 4 * 1024;

    /// <summary>
    /// Whitespace allowed between a stream's declared end and <c>endstream</c>.
    /// The PDF format puts one end-of-line there; the slack covers sloppy writers
    /// without letting overlapping candidates rescan a long whitespace run.
    /// </summary>
    private const int MaxEndStreamGap = 32;

    private static readonly byte[] StreamKeyword = Encoding.ASCII.GetBytes("stream");
    private static readonly byte[] EndStreamKeyword = Encoding.ASCII.GetBytes("endstream");
    private static readonly byte[] ObjKeyword = Encoding.ASCII.GetBytes("obj");

    private static readonly Regex MetadataType = new(@"/Type\s*/Metadata\b", RegexOptions.CultureInvariant);
    private static readonly Regex AnyFilter = new(@"/Filter\b", RegexOptions.CultureInvariant);
    private static readonly Regex FlateFilter = new(
        @"/Filter\s*(?:/FlateDecode|\[\s*/FlateDecode\s*\])",
        RegexOptions.CultureInvariant);
    private static readonly Regex DirectLength = new(@"/Length\s+(\d+)(?!\s+\d+\s+R)", RegexOptions.CultureInvariant);

    /// <summary>The payload in the PDF's metadata, trimmed, or null when there is none.</summary>
    /// <exception cref="PayloadExtractionException">
    /// The metadata holds two different payloads, or more metadata than a payslip needs.
    /// </exception>
    internal static string? FindPayload(byte[] pdf, int maxPayloadLength, CancellationToken cancellationToken)
    {
        var scanner = new StreamScanner(pdf, cancellationToken);
        string? found = null;
        int streams = 0;
        long decodedBytes = 0;
        while (scanner.TryNextMetadataStream(out string dictionary, out byte[] data))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++streams > MaxMetadataStreams)
            {
                throw TooMuchMetadata();
            }

            byte[]? decoded = Decode(data, dictionary, cancellationToken, ref decodedBytes);
            if (decoded is null)
            {
                continue;
            }

            foreach (string payload in PayloadsInXmp(decoded))
            {
                // A value this long cannot be a Verifiabl payload.
                if (payload.Length > maxPayloadLength)
                {
                    continue;
                }

                if (found is null)
                {
                    found = payload;
                }
                else if (!VerifiablPayloadText.AreSamePayload(found, payload))
                {
                    // An edited PDF can keep an earlier metadata stream. Do not guess which is current.
                    throw new PayloadExtractionException(
                        PayloadExtractionFailure.Ambiguous,
                        "The PDF metadata holds more than one different Verifiabl payload. Verify its QR code instead.");
                }
            }
        }

        return found;
    }

    private static PayloadExtractionException TooMuchMetadata() =>
        new(PayloadExtractionFailure.LimitExceeded, "The PDF holds more metadata than a payslip needs. Verify its QR code instead.");

    /// <summary>
    /// Decode a stream's data and count its decoded size against the PDF's
    /// budget, or null when the filter is unsupported or the data is invalid.
    /// </summary>
    private static byte[]? Decode(
        byte[] data,
        string dictionary,
        CancellationToken cancellationToken,
        ref long decodedBytes)
    {
        byte[]? decoded;
        if (!AnyFilter.IsMatch(dictionary))
        {
            decoded = data;
            decodedBytes += data.Length;
        }
        else if (FlateFilter.IsMatch(dictionary) && !dictionary.Contains("/Predictor"))
        {
            // Count what was inflated even when the stream is then skipped as too large.
            decoded = Inflate(data, cancellationToken, out int inflated);
            decodedBytes += inflated;
        }
        else
        {
            // Other filters and predictors do not occur in XMP metadata in practice.
            return null;
        }

        if (decodedBytes > MaxTotalMetadataBytes)
        {
            throw TooMuchMetadata();
        }

        return decoded;
    }

    /// <summary>
    /// Inflate zlib data, or null when it is invalid or larger than one stream may be.
    /// <paramref name="inflated"/> is the work done either way.
    /// </summary>
    private static byte[]? Inflate(byte[] zlib, CancellationToken cancellationToken, out int inflated)
    {
        // FlateDecode is zlib: a two-byte header, then raw deflate. DeflateStream
        // reads raw deflate on every target framework.
        const int ZlibHeaderLength = 2;
        inflated = 0;
        if (zlib.Length <= ZlibHeaderLength)
        {
            return null;
        }

        try
        {
            using var input = new MemoryStream(zlib, ZlibHeaderLength, zlib.Length - ZlibHeaderLength);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            byte[] buffer = new byte[16 * 1024];
            int read;
            while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                inflated += read;
                if (inflated > MaxMetadataBytes)
                {
                    return null;
                }

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static List<string> PayloadsInXmp(byte[] xmp)
    {
        var payloads = new List<string>();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxMetadataBytes,
            IgnoreComments = true,
        };

        try
        {
            using var stream = new MemoryStream(xmp);
            using var reader = XmlReader.Create(stream, settings);
            bool advanced = reader.Read();
            while (advanced)
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    advanced = reader.Read();
                    continue;
                }

                AddAttributePayloads(reader, payloads);
                if (IsPayloadName(reader.NamespaceURI, reader.LocalName) && !reader.IsEmptyElement)
                {
                    // Reading the content moves the reader past the element.
                    AddPayload(reader.ReadElementContentAsString(), payloads);
                    advanced = !reader.EOF;
                }
                else
                {
                    advanced = reader.Read();
                }
            }
        }
        catch (XmlException)
        {
            // Not well-formed XMP, or a payload that is not plain text.
        }

        return payloads;
    }

    private static void AddAttributePayloads(XmlReader reader, List<string> payloads)
    {
        if (!reader.HasAttributes)
        {
            return;
        }

        while (reader.MoveToNextAttribute())
        {
            if (IsPayloadName(reader.NamespaceURI, reader.LocalName))
            {
                AddPayload(reader.Value, payloads);
            }
        }

        reader.MoveToElement();
    }

    private static void AddPayload(string value, List<string> payloads)
    {
        string trimmed = value.Trim();
        if (trimmed.Length > 0)
        {
            payloads.Add(trimmed);
        }
    }

    private static bool IsPayloadName(string namespaceUri, string localName) =>
        namespaceUri == PayloadNamespace && localName == PayloadProperty;

    private static int IndexOf(byte[] haystack, byte[] needle, int start, int end)
    {
        for (int index = Math.Max(start, 0); index <= end - needle.Length; index++)
        {
            if (MatchesAt(haystack, needle, index))
            {
                return index;
            }
        }

        return -1;
    }

    private static int LastIndexOf(byte[] haystack, byte[] needle, int start, int end)
    {
        for (int index = end - needle.Length; index >= start; index--)
        {
            if (MatchesAt(haystack, needle, index))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool MatchesAt(byte[] haystack, byte[] needle, int index)
    {
        if (index < 0 || index > haystack.Length - needle.Length)
        {
            return false;
        }

        for (int offset = 0; offset < needle.Length; offset++)
        {
            if (haystack[index + offset] != needle[offset])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWhitespace(byte value) =>
        value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f' or 0;

    private static byte[] Slice(byte[] source, int start, int length)
    {
        byte[] slice = new byte[length];
        Buffer.BlockCopy(source, start, slice, 0, length);
        return slice;
    }

    private static string Latin1(byte[] source, int start, int length)
    {
        var text = new StringBuilder(length);
        for (int index = start; index < start + length; index++)
        {
            text.Append((char)source[index]);
        }

        return text.ToString();
    }

    /// <summary>
    /// Walks a PDF's stream objects front to back. Every search moves forward and
    /// each dictionary is read from bytes no earlier dictionary used, so the whole
    /// scan reads each byte a bounded number of times however malformed the PDF.
    /// </summary>
    private sealed class StreamScanner
    {
        private readonly byte[] _pdf;
        private readonly CancellationToken _cancellationToken;

        /// <summary>Where the search for the next <c>stream</c> keyword starts.</summary>
        private int _position;

        /// <summary>The earliest byte the next dictionary may use.</summary>
        private int _dictionaryFloor;

        /// <summary>
        /// The last <c>endstream</c> found, so later searches never rescan. -1 when
        /// none remains after <see cref="_endStreamSearchedFrom"/>.
        /// </summary>
        private int _nextEndStream = -1;

        private int _endStreamSearchedFrom = -1;

        internal StreamScanner(byte[] pdf, CancellationToken cancellationToken)
        {
            _pdf = pdf;
            _cancellationToken = cancellationToken;
        }

        internal bool TryNextMetadataStream(out string dictionary, out byte[] data)
        {
            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                int keyword = IndexOf(_pdf, StreamKeyword, _position, _pdf.Length);
                if (keyword < 0)
                {
                    dictionary = string.Empty;
                    data = [];
                    return false;
                }

                _position = keyword + StreamKeyword.Length;
                int dataStart = StreamDataStart(_position);
                if (dataStart < 0 || MatchesAt(_pdf, EndStreamKeyword, keyword - 3))
                {
                    continue;
                }

                string? candidate = DictionaryBefore(keyword);
                _dictionaryFloor = _position;
                if (candidate is null || !MetadataType.IsMatch(candidate))
                {
                    continue;
                }

                byte[]? raw = StreamData(candidate, dataStart);
                if (raw is null)
                {
                    continue;
                }

                _position = dataStart + raw.Length;
                _dictionaryFloor = _position;
                dictionary = candidate;
                data = raw;
                return true;
            }
        }

        /// <summary>
        /// The offset after the end-of-line that follows a <c>stream</c> keyword,
        /// or -1 when <paramref name="afterKeyword"/> is not at one.
        /// </summary>
        private int StreamDataStart(int afterKeyword)
        {
            if (afterKeyword < _pdf.Length && _pdf[afterKeyword] == '\n')
            {
                return afterKeyword + 1;
            }

            if (afterKeyword + 1 < _pdf.Length && _pdf[afterKeyword] == '\r' && _pdf[afterKeyword + 1] == '\n')
            {
                return afterKeyword + 2;
            }

            return -1;
        }

        /// <summary>
        /// The text between the last <c>obj</c> keyword before <paramref name="keyword"/>
        /// and the keyword, as Latin-1, or null when there is none close enough.
        /// </summary>
        private string? DictionaryBefore(int keyword)
        {
            int start = Math.Max(_dictionaryFloor, keyword - MaxDictionaryBytes);
            int obj = LastIndexOf(_pdf, ObjKeyword, start, keyword);
            if (obj < 0)
            {
                return null;
            }

            int dictionaryStart = obj + ObjKeyword.Length;
            return Latin1(_pdf, dictionaryStart, keyword - dictionaryStart);
        }

        private byte[]? StreamData(string dictionary, int dataStart)
        {
            Match length = DirectLength.Match(dictionary);
            if (length.Success
                && int.TryParse(length.Groups[1].Value, out int declared)
                && declared <= MaxMetadataBytes
                && declared <= _pdf.Length - dataStart
                && IsAtEndStream(dataStart + declared))
            {
                return Slice(_pdf, dataStart, declared);
            }

            // An indirect or wrong /Length: fall back to the endstream keyword.
            int endStream = NextEndStream(dataStart);
            if (endStream < 0)
            {
                return null;
            }

            int dataEnd = endStream;
            if (dataEnd > dataStart && _pdf[dataEnd - 1] == '\n')
            {
                dataEnd--;
            }

            if (dataEnd > dataStart && _pdf[dataEnd - 1] == '\r')
            {
                dataEnd--;
            }

            int size = dataEnd - dataStart;
            return size > MaxMetadataBytes ? null : Slice(_pdf, dataStart, size);
        }

        private bool IsAtEndStream(int offset)
        {
            int limit = Math.Min(_pdf.Length, offset + MaxEndStreamGap);
            while (offset < limit && IsWhitespace(_pdf[offset]))
            {
                offset++;
            }

            return MatchesAt(_pdf, EndStreamKeyword, offset);
        }

        private int NextEndStream(int from)
        {
            // Searches start at increasing offsets, so a cached miss or a match at
            // or after from still answers this one.
            bool cached = _endStreamSearchedFrom >= 0
                && _endStreamSearchedFrom <= from
                && (_nextEndStream < 0 || _nextEndStream >= from);
            if (!cached)
            {
                _endStreamSearchedFrom = from;
                _nextEndStream = IndexOf(_pdf, EndStreamKeyword, from, _pdf.Length);
            }

            return _nextEndStream;
        }
    }
}
