using Verifiabl.Internal;

namespace Verifiabl;

/// <summary>
/// Builds the payload and scan URL embedded in Verifiabl barcodes, and renders
/// the branded "Secured by Verifiabl" QR badge as SVG.
/// </summary>
public static class VerifiablBarcode
{
    /// <summary>
    /// XMP namespace for the PDF metadata copy of the barcode payload. The
    /// namespace is permanent: it is embedded in already-issued PDFs, so it must
    /// not change.
    /// </summary>
    /// <remarks>
    /// Write the barcode payload (<see cref="BuildPayload(BarcodeParts)"/>) into the payslip
    /// PDF's XMP metadata in addition to the QR code, so a verifier can read the
    /// payload even when the QR itself cannot be scanned. Both hold the identical
    /// encrypted <c>1|verifiablReference|&lt;encrypted PII&gt;</c> value; never
    /// write plaintext PII to metadata, which is not encrypted. Write the value
    /// with any PDF toolchain that can set a custom XMP property; the SDK only
    /// provides the keys.
    /// </remarks>
    public const string PdfPayloadXmpNamespace = "https://verifiabl.io/ns/";

    /// <summary>XMP property name for the PDF metadata copy of the payload.</summary>
    public const string PdfPayloadXmpProperty = "payload";

    /// <summary>
    /// Build the v1 barcode payload: <c>1|&lt;verifiablReference&gt;|&lt;ciphertext&gt;</c>.
    /// </summary>
    /// <remarks>
    /// This is the bare wire format, and the value to write into the PDF's XMP
    /// metadata. For QR codes intended to be scanned by phones, prefer
    /// <see cref="BuildScanUrl"/>, which carries the same reference and
    /// ciphertext as a public scan-redirect URL.
    /// </remarks>
    public static string BuildPayload(BarcodeParts parts) =>
        BuildPayload(parts, BarcodePayloadFormat.V1);

    /// <summary>
    /// Build a barcode payload in the selected format. V2 is opt-in.
    /// </summary>
    public static string BuildPayload(BarcodeParts parts, BarcodePayloadFormat format)
    {
        if (parts is null)
        {
            throw new ArgumentNullException(nameof(parts));
        }

        string reference = VerifiablReference.Validate(
            parts.VerifiablReference,
            nameof(parts.VerifiablReference));
        string ciphertext = Validation.ValidateCiphertext(
            parts.EncryptedPii,
            nameof(parts.EncryptedPii));
        format = ValidateFormat(format, nameof(format));

        return format == BarcodePayloadFormat.V1
            ? $"1|{reference}|{ciphertext}"
            : $"2|{reference}|{VerifiablBase32.Encode(Base64Url.DecodeCanonical(ciphertext))}";
    }

    /// <summary>
    /// Build the URL encoded into Verifiabl QR codes:
    /// <c>https://verify.verifiabl.io/v/&lt;verifiablReference&gt;#1.&lt;ciphertext&gt;</c>.
    /// The scan URL sends scanners to Verifiabl instead of exposing raw ciphertext
    /// in a phone camera preview.
    /// </summary>
    /// <remarks>
    /// The ciphertext rides in the fragment, which no client transmits to a
    /// server, so it cannot reach a request log at Verifiabl or at any
    /// intermediary. Every character stays inside the URI-safe set (base64url
    /// plus <c>.</c>), which is what keeps scanners treating this as a URL and
    /// offering tap-to-open rather than showing it as plain text.
    /// </remarks>
    public static string BuildScanUrl(BarcodeParts parts, ScanUrlOptions? options = null)
    {
        options ??= new ScanUrlOptions();
        VerifiablEnvironment environment = VerifiablEndpoints.Validate(
            options.Environment,
            $"{nameof(options)}.{nameof(options.Environment)}");
        BarcodePayloadFormat format = ValidateFormat(
            options.Format,
            $"{nameof(options)}.{nameof(options.Format)}");
        string baseUrl = options.ScanBaseUrl is null
            ? format == BarcodePayloadFormat.V2
                ? VerifiablEndpoints.V2ScanBaseUrlFor(environment)
                : VerifiablEndpoints.ScanBaseUrlFor(environment)
            : NormalizeScanBaseUrl(options.ScanBaseUrl);

        if (parts is null)
        {
            throw new ArgumentNullException(nameof(parts));
        }

        string reference = VerifiablReference.Validate(
            parts.VerifiablReference,
            nameof(parts.VerifiablReference));
        string ciphertext = Validation.ValidateCiphertext(
            parts.EncryptedPii,
            nameof(parts.EncryptedPii));

        if (format == BarcodePayloadFormat.V1)
        {
            return $"{baseUrl}/v/{reference}#1.{ciphertext}";
        }

        string base32 = VerifiablBase32.Encode(Base64Url.DecodeCanonical(ciphertext));
        return $"{baseUrl}/v/{reference}#2.{base32}";
    }

    /// <summary>
    /// Render the branded Verifiabl barcode as a standalone SVG suitable for
    /// embedding in a payslip PDF.
    /// </summary>
    /// <remarks>
    /// Takes the Verifiabl reference from <see cref="Client.VerifiablClient.RegisterNonPiiAsync"/>
    /// and the encrypted PII ciphertext from <see cref="VerifiablCrypto.EncryptPii"/>.
    /// SVG scales to any size without losing quality; if your document pipeline
    /// needs a raster image, use <see cref="CreatePng"/> rather than rasterising
    /// the SVG yourself, so module edges stay crisp and scannable.
    /// </remarks>
    public static BarcodeSvgResult CreateSvg(BarcodeParts parts, BarcodeSvgOptions? options = null)
    {
        return SvgBadgeRenderer.Render(parts, options ?? new BarcodeSvgOptions());
    }

    /// <summary>
    /// Render the branded Verifiabl barcode as a PNG.
    /// </summary>
    /// <remarks>
    /// The PNG is composited deterministically from a pre-rasterised frame plus
    /// exact pixel-aligned QR modules - no vector rasteriser and no native
    /// dependencies - so the same record produces the byte-identical raster in
    /// every Verifiabl SDK. Because the frame is pre-rasterised, PNG output
    /// exists only at pixel widths 480, 720, 960 and 1440; the physical print
    /// size is set where the image is placed in the PDF. If you need a
    /// different size, prefer <see cref="CreateSvg"/>, which scales
    /// continuously. <see cref="BarcodeSvgOptions.Width"/> is ignored here;
    /// <paramref name="pixelWidth"/> controls the bitmap size.
    /// </remarks>
    /// <param name="parts">The Verifiabl reference and encrypted PII ciphertext.</param>
    /// <param name="options">Scan URL and error-correction options.</param>
    /// <param name="pixelWidth">Output bitmap width in pixels (default: 720).</param>
    public static BarcodePngResult CreatePng(
        BarcodeParts parts,
        BarcodeSvgOptions? options = null,
        int pixelWidth = 720)
    {
        return PngBadgeRenderer.Render(parts, options ?? new BarcodeSvgOptions(), pixelWidth);
    }

    internal static BarcodePayloadFormat ValidateFormat(BarcodePayloadFormat format, string paramName)
    {
        if (format != BarcodePayloadFormat.V1 && format != BarcodePayloadFormat.V2)
        {
            throw new ArgumentOutOfRangeException(paramName, format, "Format must be V1 or V2.");
        }

        return format;
    }

    private static string NormalizeScanBaseUrl(Uri scanBaseUrl)
    {
        if (!scanBaseUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("ScanBaseUrl must be an absolute URL.", nameof(scanBaseUrl));
        }

        if (scanBaseUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("ScanBaseUrl must use https.", nameof(scanBaseUrl));
        }

        return scanBaseUrl.GetLeftPart(UriPartial.Authority);
    }
}
