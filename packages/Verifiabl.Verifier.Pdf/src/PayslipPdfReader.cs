using Verifiabl.Verifier.Extraction;
using Verifiabl.Verifier.Pdf.Internal;

namespace Verifiabl.Verifier.Pdf;

/// <summary>Reads the Verifiabl payload in a payslip PDF: its metadata payload, or its QR code when it has none.</summary>
/// <remarks>
/// The metadata payload is the XMP property <c>payload</c> in the
/// <c>https://verifiabl.io/ns/</c> namespace. Only when the PDF has none are its
/// embedded images, such as a PNG badge or a scanned page, read for a QR code. A
/// QR code drawn as vector shapes is not found; the metadata payload covers most
/// such PDFs. Nothing is rendered and the payload is returned unchanged.
/// </remarks>
public static class PayslipPdfReader
{
    /// <summary>The largest PDF read: 20 MiB, the same limit as the Verifiabl portal.</summary>
    public const int MaxPdfBytes = 20 * 1024 * 1024;

    /// <summary>The most pages read. A longer PDF is refused rather than read in part.</summary>
    public const int MaxPages = 50;

    /// <summary>The most embedded images read across the PDF.</summary>
    public const int MaxImages = 200;

    /// <summary>The most decoded image pixels across the PDF, charged before each image is decoded.</summary>
    public const long MaxTotalPixels = 200_000_000;

    /// <summary>
    /// The most bytes one compressed stream may decode to: 200 MB, the largest
    /// image read at four bytes a pixel. Checked before the stream is decoded.
    /// </summary>
    public const long MaxDecodedStreamBytes = 4L * QrImage.MaxPixels;

    /// <summary>The most bytes all of a PDF's compressed streams may decode to: 256 MiB.</summary>
    public const long MaxDecodedBytes = 256L * 1024 * 1024;

    /// <summary>Read the payload on the calling thread, without a time budget.</summary>
    /// <param name="pdf">The PDF bytes, at most <see cref="MaxPdfBytes"/>.</param>
    /// <param name="options">Optional settings, such as a JPEG decoder. <see cref="PdfReadOptions.Timeout"/> is not applied.</param>
    /// <param name="cancellationToken">Checked between metadata streams and images.</param>
    /// <returns>The payload found: the metadata payload, the QR code, or neither.</returns>
    /// <exception cref="PayloadExtractionException">
    /// The PDF is too large, holds two different metadata payloads, or has no
    /// metadata payload and has too many pages or images or two different QR codes.
    /// </exception>
    public static PdfPayloads Read(byte[] pdf, PdfReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (pdf is null)
        {
            throw new ArgumentNullException(nameof(pdf));
        }

        if (pdf.Length > MaxPdfBytes)
        {
            throw TooLarge();
        }

        options ??= new PdfReadOptions();
        string? metadata = PdfMetadataReader.FindPayload(pdf, VerifierClient.MaxBarcodeLength, cancellationToken);
        if (metadata is not null)
        {
            return new PdfPayloads(metadata, qrCode: null, skippedImages: 0, opened: true);
        }

        EmbeddedQrReader.Result images = EmbeddedQrReader.Read(pdf, options, cancellationToken);
        return new PdfPayloads(metadata: null, images.QrCode, images.SkippedImages, images.Opened);
    }

    /// <summary>Read the payload within <see cref="PdfReadOptions.Timeout"/>.</summary>
    /// <param name="pdf">The PDF bytes, at most <see cref="MaxPdfBytes"/>.</param>
    /// <param name="options">Optional settings, such as a JPEG decoder or time budget.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The payload found: the metadata payload, the QR code, or neither.</returns>
    /// <remarks>
    /// Reading runs on the thread pool through <see cref="ExtractionWork.RunAsync"/>,
    /// so the time budget ends the wait even if parsing a hostile PDF is slow.
    /// </remarks>
    /// <exception cref="PayloadExtractionException">
    /// The PDF is too large, holds two different metadata payloads, has no
    /// metadata payload and has too many pages or images or two different QR
    /// codes, or reading took longer than the time budget.
    /// </exception>
    public static async Task<PdfPayloads> ReadAsync(
        byte[] pdf,
        PdfReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (pdf is null)
        {
            throw new ArgumentNullException(nameof(pdf));
        }

        options = Validate(options);
        if (pdf.Length > MaxPdfBytes)
        {
            throw TooLarge();
        }

        // Parsing may outlive this call after a timeout, so it must not read an
        // array the caller could clear or reuse once the call returns.
        byte[] owned = (byte[])pdf.Clone();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Timeout);
        return await ExtractAsync(owned, options, deadline.Token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read the payload from a stream within <see cref="PdfReadOptions.Timeout"/>.
    /// The stream is read to its end and not disposed.
    /// </summary>
    /// <remarks>
    /// The time budget covers reading the stream and parsing it. The stream is
    /// only read, never abandoned: if it ignores cancellation, this call waits
    /// for its read to return, so the stream is not used after the call ends.
    /// </remarks>
    /// <inheritdoc cref="ReadAsync(byte[], PdfReadOptions?, CancellationToken)"/>
    public static async Task<PdfPayloads> ReadAsync(
        Stream pdf,
        PdfReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (pdf is null)
        {
            throw new ArgumentNullException(nameof(pdf));
        }

        options = Validate(options);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Timeout);

        byte[] bytes;
        try
        {
            bytes = await ReadAllAsync(pdf, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (IsDeadline(deadline.Token, cancellationToken))
        {
            throw TimedOut(options.Timeout);
        }

        return await ExtractAsync(bytes, options, deadline.Token, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PdfPayloads> ExtractAsync(
        byte[] pdf,
        PdfReadOptions options,
        CancellationToken deadline,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExtractionWork.RunAsync(token => Read(pdf, options, token), deadline).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (IsDeadline(deadline, cancellationToken))
        {
            throw TimedOut(options.Timeout);
        }
    }

    /// <summary>
    /// Whether a cancellation came from the time budget. Cancellation from the
    /// caller, a stream or a JPEG decoder is left as it was thrown.
    /// </summary>
    private static bool IsDeadline(CancellationToken deadline, CancellationToken cancellationToken) =>
        deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested;

    private static PdfReadOptions Validate(PdfReadOptions? options)
    {
        options ??= new PdfReadOptions();
        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("Timeout must be positive.", $"{nameof(options)}.{nameof(options.Timeout)}");
        }

        return options;
    }

    private static async Task<byte[]> ReadAllAsync(Stream pdf, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        while (true)
        {
            // A stream may ignore the token, so check it around each read: an
            // in-flight read is awaited, but no new read starts after cancellation.
            cancellationToken.ThrowIfCancellationRequested();
            int read = await pdf.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (read == 0)
            {
                break;
            }

            // Stop before buffering more than the limit, so an oversized upload is never held in full.
            if (buffer.Length + read > MaxPdfBytes)
            {
                throw TooLarge();
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static PayloadExtractionException TooLarge() =>
        new(PayloadExtractionFailure.LimitExceeded, $"The PDF is larger than {MaxPdfBytes} bytes.");

    private static PayloadExtractionException TimedOut(TimeSpan timeout) =>
        new(
            PayloadExtractionFailure.TimedOut,
            $"Reading the PDF did not complete within {timeout.TotalSeconds:0.###} seconds.");
}
