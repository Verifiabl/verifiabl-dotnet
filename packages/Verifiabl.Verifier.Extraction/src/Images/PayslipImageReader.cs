using Verifiabl.Verifier.Extraction.Internal;

namespace Verifiabl.Verifier.Extraction.Images;

/// <summary>Reads the Verifiabl QR code in a PNG, JPEG or WebP file, such as a photo or screenshot of a payslip.</summary>
/// <remarks>
/// The file is decoded and its QR codes are read in the WebAssembly sandbox.
/// Only QR codes shaped like a Verifiabl payload are read; other codes in the
/// image are ignored. Scan links are never fetched and the text is returned unchanged.
/// </remarks>
public static class PayslipImageReader
{
    /// <summary>The largest image file read: 20 MiB, the same limit as the Verifiabl portal.</summary>
    public const int MaxFileBytes = SandboxModule.MaxFileBytes;

    private const int ChunkBytes = 81920;

    /// <summary>Read the QR code on the calling thread, without a time budget.</summary>
    /// <param name="file">The image file's bytes, at most <see cref="MaxFileBytes"/>.</param>
    /// <param name="cancellationToken">Stops reading within about 50 milliseconds.</param>
    /// <returns>The QR code found, or an empty <see cref="ImagePayload"/>.</returns>
    /// <exception cref="PayloadExtractionException">
    /// The file is too large, cannot be decoded, or shows two different Verifiabl QR codes.
    /// </exception>
    /// <exception cref="InvalidOperationException">Wasmtime's native library could not be loaded.</exception>
    public static ImagePayload Read(byte[] file, CancellationToken cancellationToken = default)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        if (file.Length > MaxFileBytes)
        {
            throw TooLarge();
        }

        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> codes = ExtractionModule.ReadImage(file, cancellationToken);
        return new ImagePayload(QrPayloadReader.SelectPayload(codes));
    }

    /// <summary>Read the QR code within <see cref="ImageReadOptions.Timeout"/>.</summary>
    /// <param name="file">The image file's bytes, at most <see cref="MaxFileBytes"/>.</param>
    /// <param name="options">Optional settings, such as the time budget.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The QR code found, or an empty <see cref="ImagePayload"/>.</returns>
    /// <remarks>
    /// Decoding runs on the thread pool, at most one read for each processor at a
    /// time across the process, so the time budget ends the wait even if
    /// decoding a hostile file is slow.
    /// The budget starts once the sandbox's module is compiled, which the first
    /// read in a process waits for; see <see cref="SandboxModule.LoadAsync"/>.
    /// </remarks>
    /// <exception cref="PayloadExtractionException">
    /// The file is too large, cannot be decoded, shows two different Verifiabl
    /// QR codes, or reading took longer than the time budget.
    /// </exception>
    /// <exception cref="InvalidOperationException">Wasmtime's native library could not be loaded.</exception>
    public static async Task<ImagePayload> ReadAsync(
        byte[] file,
        ImageReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        options = Validate(options);
        if (file.Length > MaxFileBytes)
        {
            throw TooLarge();
        }

        // Decoding may outlive this call after a timeout, so it must not read an
        // array the caller could clear or reuse once the call returns.
        byte[] owned = (byte[])file.Clone();
        await SandboxModule.LoadAsync(cancellationToken).ConfigureAwait(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Timeout);
        return await ExtractAsync(owned, options, deadline.Token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read the QR code from a stream within <see cref="ImageReadOptions.Timeout"/>.
    /// The stream is read to its end and not disposed.
    /// </summary>
    /// <remarks>
    /// The time budget covers reading the stream and decoding it. The stream is
    /// only read, never abandoned: if it ignores cancellation, this call waits
    /// for its read to return, so the stream is not used after the call ends.
    /// </remarks>
    /// <inheritdoc cref="ReadAsync(byte[], ImageReadOptions?, CancellationToken)"/>
    public static async Task<ImagePayload> ReadAsync(
        Stream file,
        ImageReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        options = Validate(options);
        await SandboxModule.LoadAsync(cancellationToken).ConfigureAwait(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Timeout);

        byte[] bytes;
        try
        {
            bytes = await ReadAllAsync(file, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (IsDeadline(deadline.Token, cancellationToken))
        {
            throw TimedOut(options.Timeout);
        }

        return await ExtractAsync(bytes, options, deadline.Token, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ImagePayload> ExtractAsync(
        byte[] file,
        ImageReadOptions options,
        CancellationToken deadline,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExtractionWork.Shared.RunAsync(token => Read(file, token), deadline).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (IsDeadline(deadline, cancellationToken))
        {
            throw TimedOut(options.Timeout);
        }
    }

    /// <summary>Whether a cancellation came from the time budget rather than the caller or a stream.</summary>
    private static bool IsDeadline(CancellationToken deadline, CancellationToken cancellationToken) =>
        deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested;

    private static ImageReadOptions Validate(ImageReadOptions? options)
    {
        options ??= new ImageReadOptions();
        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("Timeout must be positive.", $"{nameof(options)}.{nameof(options.Timeout)}");
        }

        return options;
    }

    private static async Task<byte[]> ReadAllAsync(Stream file, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[ChunkBytes];
        while (true)
        {
            // A stream may ignore the token, so check it around each read: an
            // in-flight read is awaited, but no new read starts after cancellation.
            cancellationToken.ThrowIfCancellationRequested();
            int read = await file.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (read == 0)
            {
                break;
            }

            // Stop before buffering more than the limit, so an oversized upload is never held in full.
            if (buffer.Length + read > MaxFileBytes)
            {
                throw TooLarge();
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static PayloadExtractionException TimedOut(TimeSpan timeout) =>
        new(
            PayloadExtractionFailure.TimedOut,
            $"Reading the image did not complete within {timeout.TotalSeconds:0.###} seconds.");

    private static PayloadExtractionException TooLarge() =>
        new(PayloadExtractionFailure.LimitExceeded, $"The image file is larger than {MaxFileBytes} bytes.");
}
