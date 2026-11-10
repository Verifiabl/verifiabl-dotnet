using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;
using Verifiabl.Verifier.Extraction;

namespace Verifiabl.Verifier.Pdf.Internal;

/// <summary>
/// Finds the Verifiabl QR code in a PDF's embedded images. PdfPig parses the
/// PDF in managed code; nothing is rendered.
/// </summary>
/// <remarks>
/// The PDF is untrusted input, so pages, images and decoded pixels are capped
/// and cancellation is checked between images. Images are enumerated one at a
/// time, so the image cap stops work before later images are created. Every
/// stream PdfPig decodes, including page content, goes through
/// <see cref="BoundedFilterProvider"/>, so decompression is bounded too.
/// </remarks>
internal static class EmbeddedQrReader
{
    /// <summary>
    /// Filters allowed before DCTDecode: text encodings, decoded through the
    /// bounded provider. A decompressing filter such as FlateDecode could hide a
    /// huge stream behind a small JPEG header check, so a JPEG wrapped in one is skipped.
    /// </summary>
    private static readonly HashSet<string> JpegWrappers =
        new(StringComparer.Ordinal) { "ASCII85Decode", "A85", "ASCIIHexDecode", "AHx" };

    /// <summary>A QR code cannot be smaller than 21 modules a side.</summary>
    private const int MinQrSide = 21;

    internal sealed class Result
    {
        internal Result(string? qrCode, int skippedImages, bool opened)
        {
            QrCode = qrCode;
            SkippedImages = skippedImages;
            Opened = opened;
        }

        internal string? QrCode { get; }

        internal int SkippedImages { get; }

        /// <summary>False when PdfPig could not open the PDF at all.</summary>
        internal bool Opened { get; }
    }

    internal static Result Read(byte[] pdf, PdfReadOptions options, CancellationToken cancellationToken)
    {
        var decoded = new DecodedBytes();
        var parsing = new ParsingOptions
        {
            UseLenientParsing = true,
            SkipMissingFonts = true,
            FilterProvider = new BoundedFilterProvider(decoded),
        };

        PdfDocument document;
        try
        {
            document = PdfDocument.Open(pdf, parsing);
        }
        catch (Exception exception) when (IsParseFailure(exception))
        {
            ThrowIfExceeded(decoded);

            // Encrypted or malformed beyond repair: no images to read. The metadata may still be readable.
            return new Result(null, 0, opened: false);
        }

        using (document)
        {
            Result result = ReadImages(document, decoded, options, cancellationToken);
            ThrowIfExceeded(decoded);
            return result;
        }
    }

    /// <summary>Refuse a PDF whose decoding passed its budget, even where PdfPig swallowed the error.</summary>
    private static void ThrowIfExceeded(DecodedBytes decoded)
    {
        if (decoded.WasExceeded)
        {
            throw decoded.Exceeded();
        }
    }

    private static Result ReadImages(
        PdfDocument document,
        DecodedBytes decoded,
        PdfReadOptions options,
        CancellationToken cancellationToken)
    {
        // Reading only some pages could miss a second, different code, so refuse instead.
        if (document.NumberOfPages > PayslipPdfReader.MaxPages)
        {
            throw new PayloadExtractionException(
                PayloadExtractionFailure.LimitExceeded,
                $"The PDF has more than {PayslipPdfReader.MaxPages} pages. Verify each payslip separately.");
        }

        var budget = new Budget(new BoundedFilterProvider(decoded));
        string? found = null;
        for (int number = 1; number <= document.NumberOfPages; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            found = ReadPage(document, number, options, budget, found, cancellationToken);
            ThrowIfExceeded(decoded);
        }

        return new Result(found, budget.Skipped, opened: true);
    }

    private static string? ReadPage(
        PdfDocument document,
        int number,
        PdfReadOptions options,
        Budget budget,
        string? found,
        CancellationToken cancellationToken)
    {
        IEnumerator<IPdfImage> images;
        try
        {
            images = document.GetPage(number).GetImages().GetEnumerator();
        }
        catch (Exception exception) when (IsParseFailure(exception))
        {
            // A page PdfPig cannot parse is skipped; others may still hold the code.
            budget.Skipped++;
            return found;
        }

        using (images)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IPdfImage? image = NextImage(images, budget);
                if (image is null)
                {
                    return found;
                }

                string? payload = ReadImage(image, options, budget, cancellationToken);
                if (payload is not null)
                {
                    found = Combine(found, payload);
                }
            }
        }
    }

    /// <summary>The page's next image, counted against the image cap, or null at the end.</summary>
    private static IPdfImage? NextImage(IEnumerator<IPdfImage> images, Budget budget)
    {
        try
        {
            if (!images.MoveNext())
            {
                return null;
            }
        }
        catch (Exception exception) when (IsParseFailure(exception))
        {
            // The rest of this page's images cannot be read.
            budget.Skipped++;
            return null;
        }

        if (++budget.Images > PayslipPdfReader.MaxImages)
        {
            throw new PayloadExtractionException(
                PayloadExtractionFailure.LimitExceeded,
                "The PDF has more images than a payslip needs. Verify its QR code text instead.");
        }

        return images.Current;
    }

    private static string? ReadImage(IPdfImage image, PdfReadOptions options, Budget budget, CancellationToken cancellationToken)
    {
        if (IsTooSmall(image))
        {
            return null;
        }

        if ((long)image.WidthInSamples * image.HeightInSamples > QrImage.MaxPixels)
        {
            budget.Skipped++;
            return null;
        }

        // Charge the declared size before decoding; Decode then refuses any image
        // whose real size differs, so the charge always covers the work done.
        budget.Pixels += (long)image.WidthInSamples * image.HeightInSamples;
        if (budget.Pixels > PayslipPdfReader.MaxTotalPixels)
        {
            throw new PayloadExtractionException(
                PayloadExtractionFailure.LimitExceeded,
                "The PDF's images are larger than a payslip needs. Verify its QR code text instead.");
        }

        QrImage? pixels = Decode(image, options, budget.Filters);
        if (pixels is null)
        {
            budget.Skipped++;
            return null;
        }

        return QrPayloadReader.Read(pixels, cancellationToken);
    }

    /// <summary>
    /// The image's pixels, or null when it cannot be decoded or its real size
    /// differs from the size the PDF declares.
    /// </summary>
    private static QrImage? Decode(IPdfImage image, PdfReadOptions options, IFilterProvider filters)
    {
        QrImage? pixels = EndsWithDct(image) ? DecodeJpeg(image, options, filters) : DecodeWithPdfPig(image);
        return pixels is not null && pixels.Width == image.WidthInSamples && pixels.Height == image.HeightInSamples
            ? pixels
            : null;
    }

    private static QrImage? DecodeWithPdfPig(IPdfImage image)
    {
        try
        {
            return image.TryGetPng(out byte[]? png) && png is not null ? PngPixels.Read(png, QrImage.MaxPixels) : null;
        }
        catch (Exception exception) when (IsParseFailure(exception))
        {
            // An image in an encoding or colour space that cannot be decoded.
            return null;
        }
    }

    private static QrImage? DecodeJpeg(IPdfImage image, PdfReadOptions options, IFilterProvider filterProvider)
    {
        if (options.JpegDecoder is null)
        {
            return null;
        }

        byte[]? jpeg = JpegFile(image, filterProvider);

        // Check the JPEG's own header against the declared size before decoding it.
        if (jpeg is null || JpegHeader.Dimensions(jpeg) != (image.WidthInSamples, image.HeightInSamples))
        {
            return null;
        }

        try
        {
            return options.JpegDecoder(jpeg);
        }
        catch (ArgumentException)
        {
            // The decoder could not read this JPEG. Other failures, such as a
            // missing native library, are the integration's to fix, so they surface.
            return null;
        }
    }

    private static bool IsTooSmall(IPdfImage image) =>
        image.WidthInSamples < MinQrSide || image.HeightInSamples < MinQrSide;

    /// <summary>
    /// Whether the image's last filter is DCTDecode, the one encoding PdfPig
    /// leaves to us. Earlier filters, such as ASCII85Decode, may wrap the JPEG.
    /// </summary>
    private static bool EndsWithDct(IPdfImage image)
    {
        IReadOnlyList<NameToken> filters = FilterNames(image);
        return filters.Count > 0 && filters[filters.Count - 1].Data is "DCTDecode" or "DCT";
    }

    private static IReadOnlyList<NameToken> FilterNames(IPdfImage image)
    {
        IToken? filter = image.ImageDictionary.Data.TryGetValue(NameToken.Filter.Data, out IToken? full)
            ? full
            : image.ImageDictionary.Data.TryGetValue("F", out IToken? abbreviated) ? abbreviated : null;

        return filter switch
        {
            NameToken name => [name],
            ArrayToken array => array.Data.OfType<NameToken>().ToList(),
            _ => [],
        };
    }

    /// <summary>
    /// The JPEG file in a DCT image stream: its raw bytes after any filters that
    /// come before DCTDecode. Null when an earlier filter is not one of
    /// <see cref="JpegWrappers"/> or cannot be decoded.
    /// </summary>
    private static byte[]? JpegFile(IPdfImage image, IFilterProvider filterProvider)
    {
        IReadOnlyList<NameToken> names = FilterNames(image);
        if (names.Count == 1)
        {
            return image.RawBytes.ToArray();
        }

        try
        {
            // The bounded provider, so an ASCII85 wrapper cannot expand past the budget either.
            IReadOnlyList<IFilter> filters = filterProvider.GetNamedFilters(names);
            Memory<byte> data = image.RawMemory.ToArray();
            for (int index = 0; index < filters.Count - 1; index++)
            {
                if (!JpegWrappers.Contains(names[index].Data) || !filters[index].IsSupported)
                {
                    return null;
                }

                data = filters[index].Decode(data, image.ImageDictionary, filterProvider, index);
            }

            return data.ToArray();
        }
        catch (Exception exception) when (IsParseFailure(exception))
        {
            return null;
        }
    }

    private static string Combine(string? found, string payload)
    {
        if (found is null)
        {
            return payload;
        }

        if (!VerifiablPayloadText.AreSamePayload(found, payload))
        {
            throw new PayloadExtractionException(
                PayloadExtractionFailure.Ambiguous,
                "The PDF has more than one different Verifiabl QR code. Verify each payslip separately.");
        }

        return found;
    }

    /// <summary>A PdfPig or decoder failure on malformed content, as opposed to cancellation or a limit.</summary>
    private static bool IsParseFailure(Exception exception) =>
        exception is not OperationCanceledException and not OutOfMemoryException and not PayloadExtractionException;

    /// <summary>Running totals for one PDF.</summary>
    private sealed class Budget
    {
        internal Budget(IFilterProvider filters)
        {
            Filters = filters;
        }

        /// <summary>The PDF's bounded filters, for streams the reader decodes itself.</summary>
        internal IFilterProvider Filters { get; }

        internal int Images { get; set; }

        internal long Pixels { get; set; }

        internal int Skipped { get; set; }
    }
}
