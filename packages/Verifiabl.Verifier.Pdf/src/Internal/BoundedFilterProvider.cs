using System.IO.Compression;
using System.Runtime.InteropServices;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;
using Verifiabl.Verifier.Extraction;

namespace Verifiabl.Verifier.Pdf.Internal;

/// <summary>
/// PdfPig's filters with a decoded-byte budget for one PDF. PdfPig decodes a
/// stream in full before anyone sees its size, so a small compressed stream
/// could otherwise expand to gigabytes. Each filter's output is bounded before
/// it is allocated.
/// </summary>
/// <remarks>
/// ASCIIHex halves its input. ASCII85 output is computed from its characters,
/// since each <c>z</c> expands to four zero bytes. Flate output is counted by
/// inflating into a fixed buffer before PdfPig decodes it, RunLength output is
/// computed from its length bytes and CCITT output from its declared size. Any other filter
/// PdfPig can decode, such as LZW, cannot be bounded first, so its stream is
/// treated as unreadable and skipped.
/// </remarks>
internal sealed class BoundedFilterProvider : IFilterProvider
{
    private readonly DecodedBytes _budget;

    internal BoundedFilterProvider(DecodedBytes budget)
    {
        _budget = budget;
    }

    public IReadOnlyList<IFilter> GetFilters(DictionaryToken dictionary) =>
        Bound(DefaultFilterProvider.Instance.GetFilters(dictionary));

    public IReadOnlyList<IFilter> GetNamedFilters(IReadOnlyList<NameToken> names) =>
        Bound(DefaultFilterProvider.Instance.GetNamedFilters(names));

    public IReadOnlyList<IFilter> GetAllFilters() =>
        Bound(DefaultFilterProvider.Instance.GetAllFilters());

    private IReadOnlyList<IFilter> Bound(IReadOnlyList<IFilter> filters) =>
        filters.Select(filter => (IFilter)new BoundedFilter(filter, _budget)).ToList();

    private sealed class BoundedFilter : IFilter
    {
        private readonly IFilter _inner;
        private readonly DecodedBytes _budget;

        internal BoundedFilter(IFilter inner, DecodedBytes budget)
        {
            _inner = inner;
            _budget = budget;
        }

        public bool IsSupported => _inner.IsSupported;

        public Memory<byte> Decode(Memory<byte> input, DictionaryToken streamDictionary, IFilterProvider filterProvider, int filterIndex)
        {
            if (_inner.IsSupported)
            {
                long limit = _budget.StreamLimit;
                long? bound = UpperBound(input, streamDictionary, filterIndex, limit);
                if (bound is not long expected)
                {
                    // Not a budget failure: the stream, or the page holding it, is skipped.
                    throw new NotSupportedException("This PDF filter's output cannot be bounded before decoding.");
                }

                if (expected > limit)
                {
                    throw _budget.Exceeded();
                }
            }

            Memory<byte> output = _inner.Decode(input, streamDictionary, filterProvider, filterIndex);
            _budget.Charge(output.Length);
            return output;
        }

        /// <summary>
        /// The most bytes the filter can produce, or null when that cannot be
        /// known without decoding. Stops counting past <paramref name="limit"/>.
        /// </summary>
        private long? UpperBound(Memory<byte> input, DictionaryToken streamDictionary, int filterIndex, long limit)
        {
            return _inner switch
            {
                Ascii85Filter => Ascii85Output(input.Span),
                AsciiHexDecodeFilter => input.Length,
                FlateFilter => InflatedLength(input, limit),
                RunLengthFilter => RunLengthOutput(input.Span),
                CcittFaxDecodeFilter => CcittOutput(streamDictionary, filterIndex),
                _ => null,
            };
        }

        /// <summary>Count inflated bytes without keeping them, stopping just past the limit.</summary>
        private static long InflatedLength(Memory<byte> input, long limit)
        {
            ArraySegment<byte> bytes = MemoryMarshal.TryGetArray<byte>(input, out ArraySegment<byte> segment)
                ? segment
                : new ArraySegment<byte>(input.ToArray());

            // Skip the two-byte zlib header that PDF Flate streams carry.
            int offset = bytes.Count >= 2 && (bytes.Array![bytes.Offset] & 0x0F) == 8 ? 2 : 0;
            using var compressed = new MemoryStream(bytes.Array!, bytes.Offset + offset, bytes.Count - offset, writable: false);
            using var inflate = new DeflateStream(compressed, CompressionMode.Decompress);
            byte[] buffer = new byte[81920];
            long total = 0;
            try
            {
                int read;
                while (total <= limit && (read = inflate.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                }
            }
            catch (InvalidDataException)
            {
                // Corrupt data: PdfPig decodes no further than the valid prefix counted so far.
            }

            return total;
        }

        /// <summary>
        /// The most bytes ASCII85 text decodes to: four for each <c>z</c>, which
        /// abbreviates four zero bytes, and four for every five other characters.
        /// </summary>
        private static long Ascii85Output(ReadOnlySpan<byte> input)
        {
            long zeros = 0;
            long others = 0;
            foreach (byte character in input)
            {
                if (character == (byte)'~')
                {
                    break;
                }

                if (character == (byte)'z')
                {
                    zeros++;
                }
                else if (character > (byte)' ')
                {
                    others++;
                }
            }

            return (4 * zeros) + ((4 * others + 4) / 5);
        }

        private static long RunLengthOutput(ReadOnlySpan<byte> input)
        {
            long total = 0;
            int index = 0;
            while (index < input.Length)
            {
                int length = input[index];
                if (length == 128)
                {
                    break;
                }

                if (length < 128)
                {
                    total += length + 1;
                    index += length + 2;
                }
                else
                {
                    total += 257 - length;
                    index += 2;
                }
            }

            return total;
        }

        /// <summary>One byte per pixel for the declared size, at least the packed bitmap PdfPig produces.</summary>
        private static long? CcittOutput(DictionaryToken streamDictionary, int filterIndex)
        {
            DictionaryToken parameters = DecodeParameterResolver.GetFilterParameters(streamDictionary, filterIndex);
            long columns = Number(parameters, "Columns") ?? 1728;
            long? rows = Number(parameters, "Rows") is long declared and > 0 ? declared : Number(streamDictionary, "Height");
            return rows is long known and > 0 && columns > 0 ? columns * known : null;
        }

        private static long? Number(DictionaryToken dictionary, string key) =>
            dictionary.Data.TryGetValue(key, out IToken? token) && token is NumericToken number ? number.Long : null;
    }
}

/// <summary>Decoded bytes for one PDF, across every stream PdfPig decodes.</summary>
internal sealed class DecodedBytes
{
    private long _total;

    /// <summary>Whether a stream was refused or the total passed its limit.</summary>
    internal bool WasExceeded { get; private set; }

    /// <summary>The most one stream may produce: the per-stream cap or what is left in total.</summary>
    internal long StreamLimit =>
        Math.Min(PayslipPdfReader.MaxDecodedStreamBytes, PayslipPdfReader.MaxDecodedBytes - _total);

    internal void Charge(long bytes)
    {
        _total += bytes;
        if (_total > PayslipPdfReader.MaxDecodedBytes)
        {
            throw Exceeded();
        }
    }

    /// <summary>
    /// Record the refusal. PdfPig catches some decoding errors and carries on,
    /// so the reader checks <see cref="WasExceeded"/> too.
    /// </summary>
    internal PayloadExtractionException Exceeded()
    {
        WasExceeded = true;
        return new PayloadExtractionException(
            PayloadExtractionFailure.LimitExceeded,
            "The PDF's compressed data expands further than a payslip needs. Verify its QR code text instead.");
    }
}
