using System.Runtime.InteropServices;
using System.Text;
using static Verifiabl.Verifier.Extraction.Internal.Wasmtime.WasmtimeNative;

namespace Verifiabl.Verifier.Extraction.Internal.Wasmtime;

/// <summary>
/// A compiled module and the engine that runs it, kept for the life of the process.
/// </summary>
/// <remarks>
/// A background thread advances the engine's epoch every <see cref="TickInterval"/>.
/// Each running instance checks its cancellation token on every tick, so a
/// cancelled instance stops within about one tick. The thread is the module's
/// own, not a timer's, so a busy thread pool cannot delay cancellation.
/// </remarks>
internal sealed class WasmModule
{
    internal static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// The native stack the module's frames may use before it traps. Wasmtime
    /// measures this from where a call enters the module, not from the end of
    /// the thread's stack, so a call must start with more than this to spare:
    /// <see cref="SandboxThread"/> provides it.
    /// </summary>
    internal const int MaxWasmStackBytes = 512 * 1024;

    private readonly Dictionary<string, int> parameterCounts;

    private WasmModule(IntPtr engine, IntPtr module, Dictionary<string, int> parameterCounts)
    {
        Engine = engine;
        Module = module;
        this.parameterCounts = parameterCounts;
        // Above the threads that read files: it only sleeps and counts, and a
        // read is stopped on time only if it runs when every processor is busy.
        var ticker = new Thread(Tick)
        {
            IsBackground = true,
            Name = "Verifiabl sandbox epoch",
            Priority = ThreadPriority.Highest,
        };
        ticker.Start();
    }

    internal IntPtr Engine { get; }

    internal IntPtr Module { get; }

    /// <summary>
    /// Compile <paramref name="wasm"/> in an engine with only the features the
    /// decoder needs, and check the type of each export in <paramref name="functions"/>.
    /// </summary>
    /// <param name="wasm">The module's bytes.</param>
    /// <param name="functions">
    /// The exports that will be called, with their parameter counts. Each must
    /// take that many 32-bit integers and return one: calls are unchecked, so
    /// an export of another type would read or write past the argument buffer.
    /// </param>
    /// <exception cref="InvalidOperationException">The module does not compile, or an export is missing or has another type.</exception>
    internal static WasmModule Compile(
        byte[] wasm,
        IReadOnlyList<(string Name, int Parameters)> functions)
    {
        WasmtimeLibrary.EnsureLoaded();

        IntPtr config = wasm_config_new();
        wasmtime_config_epoch_interruption_set(config, true);

        // Explicit checks, not signal handlers: Wasmtime then installs no
        // process-wide handlers in the host, and recovering from a trap does
        // not end a Windows process that enforces hardware shadow stacks.
        wasmtime_config_signals_based_traps_set(config, false);
        wasmtime_config_max_wasm_stack_set(config, (UIntPtr)MaxWasmStackBytes);

        // ZXing-C++ catches C++ exceptions, which compile to wasm exceptions.
        wasmtime_config_wasm_exceptions_set(config, true);

        // Proposals the module does not use are attack surface.
        wasmtime_config_wasm_threads_set(config, false);
        wasmtime_config_wasm_gc_set(config, false);
        wasmtime_config_wasm_memory64_set(config, false);
        wasmtime_config_wasm_stack_switching_set(config, false);
        wasmtime_config_wasm_component_model_set(config, false);

        IntPtr engine = wasm_engine_new_with_config(config);
        if (engine == IntPtr.Zero)
        {
            throw new InvalidOperationException("Wasmtime could not create an engine.");
        }

        IntPtr module = IntPtr.Zero;
        try
        {
            IntPtr error = wasmtime_module_new(engine, wasm, (UIntPtr)wasm.Length, out module);
            WasmError.ThrowIfSet(error, "Wasmtime could not compile the extraction module");
            return new WasmModule(engine, module, CheckedParameterCounts(module, functions));
        }
        catch
        {
            if (module != IntPtr.Zero)
            {
                wasmtime_module_delete(module);
            }

            wasm_engine_delete(engine);
            throw;
        }
    }

    /// <summary>The parameter count of an export that <see cref="Compile"/> checked.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="function"/> was not checked, so calling it is not known to be safe.</exception>
    internal int ParameterCount(string function) =>
        parameterCounts.TryGetValue(function, out int count)
            ? count
            : throw new InvalidOperationException($"The extraction module's export {function} was not checked when the module was compiled.");

    private static Dictionary<string, int> CheckedParameterCounts(
        IntPtr module,
        IReadOnlyList<(string Name, int Parameters)> functions)
    {
        wasmtime_module_exports(module, out PointerVector exports);
        try
        {
            // Borrowed from the export list, so they are read before it is freed.
            var functionTypes = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
            for (int index = 0; index < Count(exports); index++)
            {
                IntPtr export = Marshal.ReadIntPtr(exports.Data, index * IntPtr.Size);
                functionTypes[Name(export)] = wasm_externtype_as_functype_const(wasm_exporttype_type(export));
            }

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach ((string name, int parameters) in functions)
            {
                if (!functionTypes.TryGetValue(name, out IntPtr type) || type == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"The extraction module has no function export named {name}.");
                }

                if (!AreAllI32(wasm_functype_params(type), parameters) || !AreAllI32(wasm_functype_results(type), 1))
                {
                    throw new InvalidOperationException(
                        $"The extraction module's export {name} does not take {parameters} 32-bit integers and return one.");
                }

                counts[name] = parameters;
            }

            return counts;
        }
        finally
        {
            wasm_exporttype_vec_delete(ref exports);
        }
    }

    private static string Name(IntPtr exportType)
    {
        ByteVector name = Marshal.PtrToStructure<ByteVector>(wasm_exporttype_name(exportType));
        byte[] bytes = new byte[checked((int)name.Size.ToUInt64())];
        Marshal.Copy(name.Data, bytes, 0, bytes.Length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static bool AreAllI32(IntPtr valueTypes, int expectedCount)
    {
        PointerVector types = Marshal.PtrToStructure<PointerVector>(valueTypes);
        if (Count(types) != expectedCount)
        {
            return false;
        }

        for (int index = 0; index < expectedCount; index++)
        {
            if (wasm_valtype_kind(Marshal.ReadIntPtr(types.Data, index * IntPtr.Size)) != ValueKindI32)
            {
                return false;
            }
        }

        return true;
    }

    private static int Count(PointerVector vector) => checked((int)vector.Size.ToUInt64());

    private void Tick()
    {
        while (true)
        {
            Thread.Sleep(TickInterval);
            wasmtime_engine_increment_epoch(Engine);
        }
    }
}

/// <summary>Reads and frees Wasmtime errors.</summary>
internal static class WasmError
{
    /// <summary>Throw <see cref="InvalidOperationException"/> with Wasmtime's message if <paramref name="error"/> is set, freeing it.</summary>
    internal static void ThrowIfSet(IntPtr error, string context)
    {
        if (error == IntPtr.Zero)
        {
            return;
        }

        string message = MessageAndDelete(error);
        throw new InvalidOperationException($"{context}: {message}");
    }

    /// <summary>Read <paramref name="error"/>'s message and free it.</summary>
    internal static string MessageAndDelete(IntPtr error)
    {
        try
        {
            wasmtime_error_message(error, out ByteVector vector);
            try
            {
                int length = checked((int)vector.Size.ToUInt64());
                byte[] bytes = new byte[length];
                Marshal.Copy(vector.Data, bytes, 0, length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                wasm_byte_vec_delete(ref vector);
            }
        }
        finally
        {
            wasmtime_error_delete(error);
        }
    }
}
