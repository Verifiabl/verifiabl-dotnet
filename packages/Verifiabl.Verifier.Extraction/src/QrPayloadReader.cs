using Verifiabl.Verifier.Extraction.Internal;
using Verifiabl.Verifier.Extraction.Internal.Wasmtime;

namespace Verifiabl.Verifier.Extraction;

/// <summary>Reads the Verifiabl QR code in pixels that you decoded with your own imaging library.</summary>
/// <remarks>
/// <para>
/// The pixels are read in the WebAssembly sandbox with ZXing-C++, as an image
/// file is, so a code reads the same from a file and from its pixels. Only QR
/// codes shaped like a Verifiabl payload are read; other codes in the image
/// are ignored. Scan links are never fetched and the text is returned unchanged.
/// </para>
/// <para>
/// The sandbox covers only the QR read. Your imaging library decodes the file
/// in your process, where the sandbox does not contain it. To decode an
/// untrusted file in the sandbox too, use
/// <see cref="Images.PayslipImageReader"/>.
/// </para>
/// </remarks>
public static class QrPayloadReader
{
    /// <summary>Read the QR code on the calling thread, without a time budget.</summary>
    /// <param name="image">The image's pixels.</param>
    /// <param name="cancellationToken">Stops reading within about 50 milliseconds.</param>
    /// <returns>The QR code text, unchanged, or null when the image has no readable Verifiabl QR code.</returns>
    /// <remarks>Copies of the same payload count once.</remarks>
    /// <exception cref="PayloadExtractionException">
    /// The image has two different Verifiabl QR codes, or reading it needs more
    /// memory or finds more QR code text than a payslip needs.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="InvalidOperationException">Wasmtime's native library could not be loaded.</exception>
    public static string? Read(QrImage image, CancellationToken cancellationToken = default)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        // Before the module is loaded, which compiles it on first use.
        cancellationToken.ThrowIfCancellationRequested();
        return Read(image, module: null, pixelsCopied: null, cancellationToken);
    }

    /// <summary>Read the QR code within <see cref="QrReadOptions.Timeout"/>.</summary>
    /// <param name="image">The image's pixels.</param>
    /// <param name="options">Optional settings, such as the time budget.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The QR code text, unchanged, or null when the image has no readable Verifiabl QR code.</returns>
    /// <remarks>
    /// Reading runs on the thread pool, at most one read for each processor at a
    /// time across the process, and the sandbox stops it when the time budget
    /// runs out. The budget starts once the sandbox's module is compiled, which
    /// the first read in a process waits for; see <see cref="SandboxModule.LoadAsync"/>.
    /// The pixels are copied into the sandbox when the read starts, so do not
    /// change the array until the call ends. The call does not end while that
    /// copy runs, so the array is yours again afterwards, whatever the outcome.
    /// </remarks>
    /// <exception cref="PayloadExtractionException">
    /// The image has two different Verifiabl QR codes, reading it needs more
    /// memory or finds more QR code text than a payslip needs, or reading took
    /// longer than the time budget.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="InvalidOperationException">Wasmtime's native library could not be loaded.</exception>
    public static Task<string?> ReadAsync(
        QrImage image,
        QrReadOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ReadAsync(image, options, module: null, cancellationToken);

    /// <summary>Read with another module, such as a stand-in that never finishes, or the extraction module when null; for tests.</summary>
    internal static async Task<string?> ReadAsync(
        QrImage image,
        QrReadOptions? options,
        WasmModule? module,
        CancellationToken cancellationToken)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        options ??= new QrReadOptions();
        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("Timeout must be positive.", $"{nameof(options)}.{nameof(options.Timeout)}");
        }

        await SandboxModule.LoadAsync(cancellationToken).ConfigureAwait(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Timeout);
        try
        {
            return await ExtractionWork.Shared
                .RunOverCallerBufferAsync((token, pixelsCopied) => Read(image, module, pixelsCopied, token), deadline.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new PayloadExtractionException(
                PayloadExtractionFailure.TimedOut,
                $"Reading the image did not complete within {options.Timeout.TotalSeconds:0.###} seconds.");
        }
    }

    private static string? Read(QrImage image, WasmModule? module, Action? pixelsCopied, CancellationToken cancellationToken)
    {
        if (module is null)
        {
            return SelectPayload(ExtractionModule.ReadPixels(
                image.Pixels,
                image.Width,
                image.Height,
                image.Format,
                cancellationToken,
                pixelsCopied));
        }

        return SelectPayload(ExtractionModule.ReadPixels(
            module,
            image.Pixels,
            image.Width,
            image.Height,
            image.Format,
            ExtractionModule.MaxMemoryBytes,
            cancellationToken,
            pixelsCopied));
    }

    /// <summary>Pick the Verifiabl payload from the QR code texts the module read.</summary>
    /// <param name="texts">Each QR code's text, unchanged.</param>
    /// <returns>The payload, unchanged, or null when no text is shaped like one.</returns>
    /// <remarks>
    /// Texts not shaped like a Verifiabl payload are ignored, and copies of one
    /// payload count once.
    /// </remarks>
    /// <exception cref="PayloadExtractionException">The texts hold two different Verifiabl payloads.</exception>
    internal static string? SelectPayload(IEnumerable<string> texts)
    {
        string? found = null;
        IEnumerable<string> candidates = texts
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
}
