using Verifiabl.Verifier.Extraction.Internal;

namespace Verifiabl.Verifier.Extraction;

/// <summary>The WebAssembly module that reads every PDF, image file and pixel buffer.</summary>
/// <remarks>
/// The decoders run in a WebAssembly module that imports nothing, so they
/// cannot open files, use the network or read the process's memory. Each read
/// gets a new instance, capped at 512 MiB, that stops soon after its time
/// budget ends or its cancellation token is cancelled. A file that makes the
/// decoders misbehave is reported as unreadable.
/// </remarks>
public static class SandboxModule
{
    /// <summary>The largest file a reader accepts: 20 MiB.</summary>
    public const int MaxFileBytes = 20 * 1024 * 1024;

    /// <summary>Compile the module now, instead of during the first read.</summary>
    /// <param name="cancellationToken">Ends the wait. Compiling continues, so a later call finds it done.</param>
    /// <returns>A task that completes when the module is ready to read files.</returns>
    /// <remarks>
    /// The first use in a process compiles the module, which takes about 2 seconds
    /// and longer on a busy machine. Later calls return at once. Each
    /// <c>ReadAsync</c> and verify method awaits this before its time budget
    /// starts, so the budget covers only the file. Call it at startup so the
    /// first file does not wait.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="InvalidOperationException">
    /// The sandbox itself could not start, such as when Wasmtime's native library
    /// could not be loaded.
    /// </exception>
    public static Task LoadAsync(CancellationToken cancellationToken = default) =>
        ExtractionModule.LoadAsync(cancellationToken);
}
