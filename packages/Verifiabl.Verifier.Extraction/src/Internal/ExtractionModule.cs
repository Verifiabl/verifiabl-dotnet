using Verifiabl.Verifier.Extraction.Internal.Wasmtime;

namespace Verifiabl.Verifier.Extraction.Internal;

/// <summary>
/// Runs the extraction module (verifiabl-extract.wasm, synced from its own
/// source) for one file or pixel buffer at a time, so a decoder bug can reach
/// only that read's instance, never the host process.
/// </summary>
/// <remarks>
/// <para>
/// The module imports nothing. Each read gets a new instance capped at
/// <see cref="MaxMemoryBytes"/>, on its own thread, which stops within about
/// one epoch tick of its token being cancelled.
/// </para>
/// <para>
/// A hostile file may take the module over, so everything it returns is
/// untrusted. A module that breaks its interface reads as
/// <see cref="ExtractionStatus.Unreadable"/>, like a damaged file.
/// </para>
/// </remarks>
internal static class ExtractionModule
{
    /// <summary>The module's own memory maximum.</summary>
    internal const long MaxMemoryBytes = 512L * 1024 * 1024;

    /// <summary>The module's page limit for a PDF without a metadata payload: <c>maxPdfPages</c> in abi/extraction-abi.json.</summary>
    internal const int MaxPdfPages = 50;

    /// <summary>The largest result read: <c>maxResultBytes</c> in abi/extraction-abi.json.</summary>
    internal const int MaxResultBytes = 1024 * 1024;

    /// <summary>
    /// The largest pixel buffer read: <c>maxPixelBytes</c> in abi/extraction-abi.json,
    /// which is <see cref="QrImage.MaxPixels"/> pixels of 4 bytes.
    /// </summary>
    internal const int MaxPixelBytes = 4 * QrImage.MaxPixels;

    private const string ModuleResource = "Verifiabl.Verifier.Extraction.verifiabl-extract.wasm";

    // The abi/extraction-abi.json version this host implements.
    private const int AbiVersion = 3;

    // The exports this host calls, with their parameter counts.
    private static readonly (string Name, int Parameters)[] Exports =
    [
        ("vf_abi_version", 0),
        ("vf_alloc", 1),
        ("vf_read_image", 3),
        ("vf_read_pdf", 3),
        ("vf_read_pixels", 6),
        ("vf_result_ptr", 0),
        ("vf_result_len", 0),
        ("vf_out_of_memory", 0),
    ];

    private static readonly Lazy<WasmModule> Shared = new(
        () => Compile(ReadModule()),
        LazyThreadSafetyMode.ExecutionAndPublication);

    // One compilation for every caller that waits, on a thread of its own:
    // it runs for seconds and must not depend on a free thread pool thread.
    private static readonly Lazy<Task> Loading = new(StartLoading, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The text of every QR code in an image file.</summary>
    internal static IReadOnlyList<string> ReadImage(byte[] file, CancellationToken cancellationToken) =>
        ReadImage(LoadedModule(), file, MaxMemoryBytes, cancellationToken);

    /// <summary>Read with a lower memory cap than <see cref="MaxMemoryBytes"/>; for tests.</summary>
    internal static IReadOnlyList<string> ReadImage(byte[] file, long maxMemoryBytes, CancellationToken cancellationToken) =>
        ReadImage(LoadedModule(), file, maxMemoryBytes, cancellationToken);

    /// <summary>Read with another module, such as a stand-in that misbehaves; for tests.</summary>
    internal static IReadOnlyList<string> ReadImage(
        WasmModule module,
        byte[] file,
        long maxMemoryBytes,
        CancellationToken cancellationToken) =>
        RunFile(module, file, "vf_read_image", maxMemoryBytes, cancellationToken, ExtractionResult.ParseCodes);

    /// <summary>A PDF's metadata payloads or, when it has none, the QR codes on its rendered pages.</summary>
    internal static SandboxedPdfPayloads ReadPdf(byte[] file, CancellationToken cancellationToken) =>
        ReadPdf(LoadedModule(), file, cancellationToken);

    /// <inheritdoc cref="ReadImage(WasmModule, byte[], long, CancellationToken)"/>
    internal static SandboxedPdfPayloads ReadPdf(WasmModule module, byte[] file, CancellationToken cancellationToken) =>
        RunFile(module, file, "vf_read_pdf", MaxMemoryBytes, cancellationToken, ExtractionResult.ParsePdf);

    /// <summary>
    /// The text of every QR code in pixels that the caller decoded: rows from
    /// top to bottom with no padding, <paramref name="width"/> by
    /// <paramref name="height"/> pixels of <paramref name="format"/>.
    /// </summary>
    /// <remarks>
    /// The module checks that the buffer's length matches its size, and reads
    /// the codes with the reader it uses for image files.
    /// </remarks>
    /// <param name="pixels">The caller's array, which is read only until it is copied into the instance.</param>
    /// <param name="width">The image's width in pixels.</param>
    /// <param name="height">The image's height in pixels.</param>
    /// <param name="format">How each pixel is stored.</param>
    /// <param name="cancellationToken">Stops the read within about one epoch tick.</param>
    /// <param name="pixelsCopied">Called once <paramref name="pixels"/> will not be read again, or null.</param>
    internal static IReadOnlyList<string> ReadPixels(
        byte[] pixels,
        int width,
        int height,
        PixelFormat format,
        CancellationToken cancellationToken,
        Action? pixelsCopied = null) =>
        ReadPixels(LoadedModule(), pixels, width, height, format, MaxMemoryBytes, cancellationToken, pixelsCopied);

    /// <summary>Read with a lower memory cap than <see cref="MaxMemoryBytes"/>; for tests.</summary>
    internal static IReadOnlyList<string> ReadPixels(
        byte[] pixels,
        int width,
        int height,
        PixelFormat format,
        long maxMemoryBytes,
        CancellationToken cancellationToken) =>
        ReadPixels(LoadedModule(), pixels, width, height, format, maxMemoryBytes, cancellationToken);

    /// <inheritdoc cref="ReadImage(WasmModule, byte[], long, CancellationToken)"/>
    internal static IReadOnlyList<string> ReadPixels(
        WasmModule module,
        byte[] pixels,
        int width,
        int height,
        PixelFormat format,
        long maxMemoryBytes,
        CancellationToken cancellationToken,
        Action? pixelsCopied = null)
    {
        // Before an instance exists, so the module never allocates for an oversized
        // buffer. One this large holds more than MaxPixels pixels in every format.
        if (pixels.Length > MaxPixelBytes)
        {
            throw FailureFor(ExtractionStatus.TooManyPixels);
        }

        int[] shape = [width, height, (int)format, QrImage.MaxPixels];
        return Run(module, pixels, "vf_read_pixels", shape, maxMemoryBytes, cancellationToken, ExtractionResult.ParseCodes, pixelsCopied);
    }

    /// <summary>Compile the module unless it already is, and wait until it is ready or the token is cancelled.</summary>
    /// <exception cref="InvalidOperationException">Wasmtime's native library could not be loaded.</exception>
    internal static async Task LoadAsync(CancellationToken cancellationToken)
    {
        Task loading = Loading.Value;
        if (!loading.IsCompleted)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(loading, cancelled.Task).ConfigureAwait(false) != loading)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }

        await loading.ConfigureAwait(false);
    }

    /// <summary>What a read of a file over <see cref="SandboxModule.MaxFileBytes"/> throws.</summary>
    internal static PayloadExtractionException FileTooLarge() => new(
        PayloadExtractionFailure.LimitExceeded,
        $"The file is larger than {SandboxModule.MaxFileBytes} bytes.");

    /// <summary>Compile a module with the extraction module's exports.</summary>
    /// <exception cref="InvalidOperationException">It does not compile, or lacks an export of the type this host calls.</exception>
    internal static WasmModule Compile(byte[] wasm) => WasmModule.Compile(wasm, Exports);

    /// <summary>What a read that ended with <paramref name="status"/> throws.</summary>
    internal static PayloadExtractionException FailureFor(ExtractionStatus status) => status switch
    {
        ExtractionStatus.Unreadable => new(
            PayloadExtractionFailure.Unreadable,
            "The image or PDF cannot be read."),
        ExtractionStatus.TooManyPixels => new(
            PayloadExtractionFailure.LimitExceeded,
            $"The image has more than {QrImage.MaxPixels} pixels."),
        ExtractionStatus.OutOfMemory => new(
            PayloadExtractionFailure.LimitExceeded,
            "Reading needed more memory than the sandbox allows."),
        ExtractionStatus.TooManyPages => new(
            PayloadExtractionFailure.LimitExceeded,
            $"The PDF has more than {MaxPdfPages} pages and no metadata payload."),
        ExtractionStatus.TooMuchMetadata => new(
            PayloadExtractionFailure.LimitExceeded,
            "The PDF holds more metadata than a payslip needs. Verify its QR code instead."),
        ExtractionStatus.ResultTooLarge => new(
            PayloadExtractionFailure.LimitExceeded,
            "The QR codes hold more text than a payslip needs."),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Only a failed read throws."),
    };

    private static T RunFile<T>(
        WasmModule module,
        byte[] file,
        string export,
        long maxMemoryBytes,
        CancellationToken cancellationToken,
        Func<byte[], T?> parseResult)
        where T : class
    {
        // Before an instance exists, so the module never allocates for an oversized file.
        if (file.Length > SandboxModule.MaxFileBytes)
        {
            throw FileTooLarge();
        }

        return Run(module, file, export, [QrImage.MaxPixels], maxMemoryBytes, cancellationToken, parseResult);
    }

    // Runs the export on the input in a new instance. The arguments are what
    // the export takes after the input's address and length. inputCopied is
    // called once the input will not be read again.
    private static T Run<T>(
        WasmModule module,
        byte[] input,
        string export,
        int[] arguments,
        long maxMemoryBytes,
        CancellationToken cancellationToken,
        Func<byte[], T?> parseResult,
        Action? inputCopied = null)
        where T : class
    {
        // A short read can finish before the first epoch tick checks the token.
        cancellationToken.ThrowIfCancellationRequested();
        (ExtractionStatus status, T? result) = SandboxThread.Run(
            () => Read(module, input, export, arguments, maxMemoryBytes, cancellationToken, parseResult, inputCopied));

        // A read can also end between the token's cancellation and the next tick.
        cancellationToken.ThrowIfCancellationRequested();
        return result ?? throw FailureFor(status);
    }

    private static (ExtractionStatus Status, T? Result) Read<T>(
        WasmModule module,
        byte[] input,
        string export,
        int[] arguments,
        long maxMemoryBytes,
        CancellationToken cancellationToken,
        Func<byte[], T?> parseResult,
        Action? inputCopied)
        where T : class
    {
        using var instance = new WasmInstance(module, maxMemoryBytes, cancellationToken);
        try
        {
            // Before the input is written, so a mismatch is the package's fault, not the input's.
            int abiVersion = instance.Call("vf_abi_version");
            if (abiVersion != AbiVersion)
            {
                throw new InvalidOperationException($"The extraction module implements ABI {abiVersion}; this package needs {AbiVersion}.");
            }

            int address = instance.Call("vf_alloc", input.Length);
            bool written = instance.TryWrite(address, input);

            // The module reads its own copy from here on, so a caller that owns the input may change it.
            inputCopied?.Invoke();
            if (!written)
            {
                return (ExtractionStatus.Unreadable, null);
            }

            int status = instance.Call(export, [address, input.Length, .. arguments]);
            if (status is not ((int)ExtractionStatus.Ok or (int)ExtractionStatus.Partial))
            {
                return (Enum.IsDefined(typeof(ExtractionStatus), status) ? (ExtractionStatus)status : ExtractionStatus.Unreadable, null);
            }

            byte[]? json = ReadResult(instance);
            T? result = json is null ? null : parseResult(json);
            return result is null ? (ExtractionStatus.Unreadable, null) : ((ExtractionStatus)status, result);
        }
        catch (WasmTrapException)
        {
            // A decoder panicked on a file it did not expect, or an allocation failed at the memory cap.
            return (RanOutOfMemory(instance) ? ExtractionStatus.OutOfMemory : ExtractionStatus.Unreadable, null);
        }
    }

    /// <summary>The result's bytes, or null when it is larger than a result may be or outside the memory.</summary>
    private static byte[]? ReadResult(WasmInstance instance)
    {
        // Checked before anything is copied: the length is the module's word alone.
        int length = instance.Call("vf_result_len");
        if (length < 0 || length > MaxResultBytes)
        {
            return null;
        }

        return instance.Read(instance.Call("vf_result_ptr"), length);
    }

    private static bool RanOutOfMemory(WasmInstance instance)
    {
        try
        {
            return instance.Call("vf_out_of_memory") != 0;
        }
        catch (WasmTrapException)
        {
            return false;
        }
    }

    private static Task StartLoading()
    {
        Task loading = Task.Factory.StartNew(
            () => _ = LoadedModule(),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        // A caller that stopped waiting leaves a failure unobserved; the next caller still receives it.
        _ = loading.ContinueWith(
            failed => failed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return loading;
    }

    private static WasmModule LoadedModule()
    {
        try
        {
            return Shared.Value;
        }
        catch (Exception exception) when (IsMissingNativeLibrary(exception))
        {
            throw new InvalidOperationException(
                "Wasmtime's native library could not be loaded. It ships for Windows, macOS and "
                + "glibc Linux on x64 and Arm64, for 64-bit processes.",
                exception);
        }
    }

    private static bool IsMissingNativeLibrary(Exception exception) =>
        exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
        || exception is TypeInitializationException { InnerException: DllNotFoundException or EntryPointNotFoundException };

    private static byte[] ReadModule()
    {
        using Stream stream = typeof(ExtractionModule).Assembly.GetManifestResourceStream(ModuleResource)
            ?? throw new InvalidOperationException($"The embedded resource {ModuleResource} is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
