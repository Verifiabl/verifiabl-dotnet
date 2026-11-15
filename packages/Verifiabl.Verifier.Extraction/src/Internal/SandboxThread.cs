using System.Runtime.ExceptionServices;
using Verifiabl.Verifier.Extraction.Internal.Wasmtime;

namespace Verifiabl.Verifier.Extraction.Internal;

/// <summary>Runs a read on a thread whose stack is known to hold the module's.</summary>
/// <remarks>
/// The module may use <see cref="WasmModule.MaxWasmStackBytes"/> of stack before
/// Wasmtime traps it. A caller's thread may have less than that left, and a
/// thread that runs out first ends the process with a stack overflow instead
/// of a trap. Each read therefore gets a new thread of <see cref="StackBytes"/>.
/// </remarks>
internal static class SandboxThread
{
    /// <summary>Four times the module's stack, which leaves the rest for Wasmtime and the runtime's own frames.</summary>
    internal const int StackBytes = 2 * 1024 * 1024;

    /// <summary>Run <paramref name="read"/> on a new thread and wait for it.</summary>
    /// <returns>What <paramref name="read"/> returned.</returns>
    /// <remarks>An exception from <paramref name="read"/> is rethrown here with its stack trace.</remarks>
    internal static T Run<T>(Func<T> read)
    {
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    result = read();
                }
                catch (Exception exception)
                {
                    // Unhandled on this thread, it would end the process.
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
            },
            StackBytes)
        {
            IsBackground = true,
            Name = "Verifiabl extraction",
        };
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result;
    }
}
