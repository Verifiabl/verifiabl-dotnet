using Verifiabl.Verifier.Extraction.Internal;

namespace Verifiabl.Verifier.Extraction.Pdf;

/// <summary>Reads the Verifiabl payload in a payslip PDF: its metadata payload, or its QR code when it has none.</summary>
/// <remarks>
/// <para>
/// The metadata payload is the XMP property <c>payload</c> in the
/// <c>https://verifiabl.io/ns/</c> namespace. Only when the PDF has none are its
/// pages rendered and read for a QR code, so a code drawn as vector shapes is
/// found as well as one in an image. The payload is returned unchanged.
/// </para>
/// <para>
/// PDFs are untrusted, so they are read in a WebAssembly sandbox with no
/// access to the host process.
/// </para>
/// </remarks>
public static class PayslipPdfReader
{
    /// <summary>The largest PDF read: 20 MiB, the same limit as the Verifiabl portal.</summary>
    public const int MaxPdfBytes = SandboxModule.MaxFileBytes;

    /// <summary>The most pages rendered. A longer PDF without a metadata payload is refused rather than read in part.</summary>
    public const int MaxPages = 50;

    /// <summary>Read the payload on the calling thread, without a time budget.</summary>
    /// <param name="pdf">The PDF bytes, at most <see cref="MaxPdfBytes"/>.</param>
    /// <param name="cancellationToken">Stops reading within about 50 milliseconds.</param>
    /// <returns>The payload found: the metadata payload, the QR code, or neither.</returns>
    /// <exception cref="PayloadExtractionException">
    /// The PDF is too large, cannot be read, holds two different metadata
    /// payloads, or has no metadata payload and has too many pages or two
    /// different QR codes.
    /// </exception>
    /// <exception cref="InvalidOperationException">Wasmtime's native library could not be loaded.</exception>
    public static PdfPayloads Read(byte[] pdf, CancellationToken cancellationToken = default)
    {
        if (pdf is null)
        {
            throw new ArgumentNullException(nameof(pdf));
        }

        if (pdf.Length > MaxPdfBytes)
        {
            throw TooLarge();
        }

        cancellationToken.ThrowIfCancellationRequested();
        SandboxedPdfPayloads found = ExtractionModule.ReadPdf(pdf, cancellationToken);
        string? metadata = MetadataPayload(found.MetadataPayloads);
        return metadata is not null
            ? new PdfPayloads(metadata, qrCode: null)
            : new PdfPayloads(metadata: null, QrPayloadReader.SelectPayload(found.QrCodes), found.Pages);
    }

    /// <summary>Read the payload within <see cref="PdfReadOptions.Timeout"/>.</summary>
    /// <param name="pdf">The PDF bytes, at most <see cref="MaxPdfBytes"/>.</param>
    /// <param name="options">Optional settings, such as the time budget.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The payload found: the metadata payload, the QR code, or neither.</returns>
    /// <remarks>
    /// Reading runs on the thread pool, at most one read for each processor at a
    /// time across the process, and the sandbox stops reading when the time
    /// budget runs out.
    /// The budget starts once the sandbox's module is compiled, which the first
    /// read in a process waits for; see <see cref="SandboxModule.LoadAsync"/>.
    /// </remarks>
    /// <exception cref="PayloadExtractionException">
    /// The PDF is too large, cannot be read, holds two different metadata
    /// payloads, has no metadata payload and has too many pages or two different
    /// QR codes, or reading took longer than the time budget.
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
        await SandboxModule.LoadAsync(cancellationToken).ConfigureAwait(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Timeout);
        return await ExtractAsync(owned, options, deadline.Token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read the payload from a stream within <see cref="PdfReadOptions.Timeout"/>.
    /// The stream is read to its end and not disposed.
    /// </summary>
    /// <remarks>
    /// The time budget covers reading the stream and the PDF. The stream is
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
        await SandboxModule.LoadAsync(cancellationToken).ConfigureAwait(false);
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
            return await ExtractionWork.Shared.RunAsync(token => Read(pdf, token), deadline).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (IsDeadline(deadline, cancellationToken))
        {
            throw TimedOut(options.Timeout);
        }
    }

    /// <summary>
    /// Whether a cancellation came from the time budget. Cancellation from the
    /// caller or a stream is left as it was thrown.
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

    /// <summary>
    /// The metadata payload: values too long to be one are ignored, and copies of
    /// one payload count once.
    /// </summary>
    /// <exception cref="PayloadExtractionException">The metadata holds two different payloads.</exception>
    private static string? MetadataPayload(IReadOnlyList<string> values)
    {
        string? found = null;
        foreach (string value in values.Where(value => value.Length <= VerifierClient.MaxBarcodeLength))
        {
            if (found is null)
            {
                found = value;
            }
            else if (!VerifiablPayloadText.AreSamePayload(found, value))
            {
                // An edited PDF can keep an earlier metadata stream. Do not guess which is current.
                throw new PayloadExtractionException(
                    PayloadExtractionFailure.Ambiguous,
                    "The PDF metadata holds more than one different Verifiabl payload. Verify its QR code instead.");
            }
        }

        return found;
    }

    private static PayloadExtractionException TooLarge() =>
        new(PayloadExtractionFailure.LimitExceeded, $"The PDF is larger than {MaxPdfBytes} bytes.");

    private static PayloadExtractionException TimedOut(TimeSpan timeout) =>
        new(
            PayloadExtractionFailure.TimedOut,
            $"Reading the PDF did not complete within {timeout.TotalSeconds:0.###} seconds.");
}
