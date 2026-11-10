using ZXing;
using ZXing.Common;

namespace Verifiabl.Verifier.Extraction;

/// <summary>Reads the Verifiabl QR code in an image's pixels with ZXing.Net.</summary>
public static class QrPayloadReader
{
    /// <summary>Read the Verifiabl QR code's text from an image.</summary>
    /// <param name="image">The image's pixels.</param>
    /// <param name="cancellationToken">Checked between decoding passes.</param>
    /// <returns>The QR code text, unchanged, or null when the image has no readable Verifiabl QR code.</returns>
    /// <remarks>
    /// QR codes that are not shaped like a Verifiabl payload are ignored. Copies
    /// of the same payload count once. This runs on the calling thread and cannot
    /// be interrupted mid-pass; use <see cref="ExtractionWork.RunAsync"/> to bound the wait.
    /// </remarks>
    /// <exception cref="PayloadExtractionException">The image has two different Verifiabl QR codes.</exception>
    public static string? Read(QrImage image, CancellationToken cancellationToken = default)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        BarcodeReaderGeneric reader = CreateReader();
        LuminanceSource source = LuminanceSourceFor(image);

        // DecodeMultiple ignores TryInverted, so read light-on-dark codes in a
        // second pass. Both passes always run, so a code of either polarity
        // counts towards the two-different-codes check.
        string? found = FindIn(reader, source, null, cancellationToken);
        return FindIn(reader, source.invert(), found, cancellationToken);
    }

    private static string? FindIn(
        BarcodeReaderGeneric reader,
        LuminanceSource source,
        string? found,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Result[]? results = reader.DecodeMultiple(source);
        cancellationToken.ThrowIfCancellationRequested();

        IEnumerable<string> candidates = (results ?? [])
            .Select(result => result.Text)
            .Where(text => text.Length <= VerifierClient.MaxBarcodeLength && VerifiablPayloadText.IsVerifiablShaped(text));
        foreach (string text in candidates)
        {
            found = Combine(found, text);
        }

        return found;
    }

    private static string Combine(string? found, string text)
    {
        if (found is null)
        {
            return text;
        }

        if (!VerifiablPayloadText.AreSamePayload(found, text))
        {
            throw new PayloadExtractionException(
                PayloadExtractionFailure.Ambiguous,
                "The image has more than one different Verifiabl QR code. Verify each payslip separately.");
        }

        return found;
    }

    private static BarcodeReaderGeneric CreateReader()
    {
        return new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                PossibleFormats = [BarcodeFormat.QR_CODE],
                TryHarder = true,
            },
        };
    }

    private static LuminanceSource LuminanceSourceFor(QrImage image)
    {
        RGBLuminanceSource.BitmapFormat format = image.Format switch
        {
            QrPixelFormat.Rgba32 => RGBLuminanceSource.BitmapFormat.RGBA32,
            QrPixelFormat.Bgra32 => RGBLuminanceSource.BitmapFormat.BGRA32,
            _ => RGBLuminanceSource.BitmapFormat.Gray8,
        };
        return new RGBLuminanceSource(image.Pixels, image.Width, image.Height, format);
    }
}
