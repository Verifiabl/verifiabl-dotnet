using System.Runtime.InteropServices;
using System.Text;
using static Verifiabl.Verifier.Extraction.Internal.Wasmtime.WasmtimeNative;

namespace Verifiabl.Verifier.Extraction.Internal.Wasmtime;

/// <summary>
/// One instance of a <see cref="WasmModule"/> in its own store, with no imports,
/// a memory cap, and a cancellation token checked at every epoch tick.
/// </summary>
/// <remarks>Not thread-safe: use an instance from one thread, then dispose it.</remarks>
internal sealed class WasmInstance : IDisposable
{
    // Native code holds only a pointer to this delegate, so it must never be collected.
    private static readonly EpochDeadlineCallback OnEpochDeadline = CheckCancellation;

    private readonly WasmModule module;
    private readonly IntPtr store;
    private readonly IntPtr context;
    private readonly GCHandle state;
    private readonly CancellationToken cancellationToken;
    private Handle instance;
    private bool cancelled;
    private bool disposed;

    /// <summary>Instantiate <paramref name="module"/> with at most <paramref name="maxMemoryBytes"/> of linear memory.</summary>
    /// <exception cref="InvalidOperationException">The module could not be instantiated. No file has reached it yet, so this is not the file's fault.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    internal WasmInstance(WasmModule module, long maxMemoryBytes, CancellationToken cancellationToken)
    {
        this.module = module;
        this.cancellationToken = cancellationToken;
        store = wasmtime_store_new(module.Engine, IntPtr.Zero, IntPtr.Zero);
        context = wasmtime_store_context(store);
        state = GCHandle.Alloc(this);
        try
        {
            wasmtime_store_limiter(store, maxMemoryBytes, tableElements: -1, instances: 1, tables: 1, memories: 1);
            wasmtime_store_epoch_deadline_callback(store, OnEpochDeadline, GCHandle.ToIntPtr(state), IntPtr.Zero);
            wasmtime_context_set_epoch_deadline(context, 1);

            IntPtr error = wasmtime_instance_new(context, module.Module, IntPtr.Zero, UIntPtr.Zero, out instance, out IntPtr trap);

            // Wasmtime's reason names the module or the limits, never a file.
            string? reason = error != IntPtr.Zero ? WasmError.MessageAndDelete(error) : null;
            if (Failed(IntPtr.Zero, trap) || reason is not null)
            {
                throw new InvalidOperationException($"The extraction module could not be instantiated: {reason ?? "it trapped"}");
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Call an exported function that takes 32-bit integers and returns one.</summary>
    /// <exception cref="WasmTrapException">The call did not return: the module trapped, or Wasmtime stopped it.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled while the function ran.</exception>
    /// <exception cref="InvalidOperationException">The function's type was not checked, or takes another number of arguments.</exception>
    internal int Call(string name, params int[] arguments)
    {
        // The call is unchecked, so a wrong count would read or write past the buffer.
        if (module.ParameterCount(name) != arguments.Length)
        {
            throw new InvalidOperationException($"The extraction module's export {name} does not take {arguments.Length} arguments.");
        }

        Handle function = Export(name, ExternFunc);

        // Arguments and the single result share one buffer.
        var values = new RawValue[Math.Max(arguments.Length, 1)];
        for (int index = 0; index < arguments.Length; index++)
        {
            values[index].I32 = arguments[index];
        }

        IntPtr error = wasmtime_func_call_unchecked(context, ref function, values, (UIntPtr)values.Length, out IntPtr trap);
        if (Failed(error, trap))
        {
            throw new WasmTrapException();
        }

        return values[0].I32;
    }

    /// <summary>Copy <paramref name="bytes"/> into linear memory at <paramref name="address"/>, checking the range first.</summary>
    /// <returns>False, writing nothing, if the range is outside the memory.</returns>
    internal bool TryWrite(int address, byte[] bytes)
    {
        IntPtr start = MemoryAt(address, bytes.Length);
        if (start == IntPtr.Zero)
        {
            return false;
        }

        Marshal.Copy(bytes, 0, start, bytes.Length);
        return true;
    }

    /// <summary>Copy <paramref name="length"/> bytes out of linear memory, checking the range first.</summary>
    /// <returns>The bytes, or <see langword="null"/> if the range is outside the memory.</returns>
    internal byte[]? Read(int address, int length)
    {
        IntPtr start = MemoryAt(address, length);
        if (start == IntPtr.Zero)
        {
            return null;
        }

        byte[] bytes = new byte[length];
        Marshal.Copy(start, bytes, 0, length);
        return bytes;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        wasmtime_store_delete(store);
        if (state.IsAllocated)
        {
            state.Free();
        }
    }

    private static IntPtr CheckCancellation(IntPtr context, IntPtr data, ref ulong deadlineDelta, ref byte updateKind)
    {
        // Exceptions must not cross into native code.
        try
        {
            var instance = (WasmInstance)GCHandle.FromIntPtr(data).Target!;
            if (instance.cancellationToken.IsCancellationRequested)
            {
                instance.cancelled = true;
                return wasmtime_error_new("cancelled");
            }

            deadlineDelta = 1;
            updateKind = UpdateDeadlineContinue;
            return IntPtr.Zero;
        }
        catch (Exception)
        {
            return wasmtime_error_new("epoch callback failed");
        }
    }

    /// <summary>A pointer to <paramref name="length"/> bytes at <paramref name="address"/>, or zero if out of range.</summary>
    private IntPtr MemoryAt(int address, int length)
    {
        Handle memory = Export("memory", ExternMemory);
        IntPtr data = wasmtime_memory_data(context, ref memory);
        ulong size = wasmtime_memory_data_size(context, ref memory).ToUInt64();
        if (address < 0 || length < 0 || (ulong)address + (ulong)length > size)
        {
            return IntPtr.Zero;
        }

        return IntPtr.Add(data, address);
    }

    private Handle Export(string name, byte kind)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(name);
        IntPtr item = Marshal.AllocHGlobal(ExternBytes);
        try
        {
            if (!wasmtime_instance_export_get(context, ref instance, encoded, (UIntPtr)encoded.Length, item)
                || Marshal.ReadByte(item) != kind)
            {
                throw new InvalidOperationException($"The extraction module has no export named {name} of the expected kind.");
            }

            return Marshal.PtrToStructure<Handle>(IntPtr.Add(item, ExternUnionOffset));
        }
        finally
        {
            Marshal.FreeHGlobal(item);
        }
    }

    /// <summary>Whether a call failed, freeing what Wasmtime returned for it.</summary>
    /// <exception cref="OperationCanceledException">It failed because the token was cancelled.</exception>
    private bool Failed(IntPtr error, IntPtr trap)
    {
        if (trap != IntPtr.Zero)
        {
            wasm_trap_delete(trap);
        }

        if (error != IntPtr.Zero)
        {
            wasmtime_error_delete(error);
        }

        if (cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return trap != IntPtr.Zero || error != IntPtr.Zero;
    }
}

/// <summary>
/// A call into the module did not return: it trapped on a panic, an
/// out-of-bounds access, a failed allocation, an exhausted stack or an uncaught
/// exception, or Wasmtime stopped it.
/// </summary>
internal sealed class WasmTrapException : Exception
{
    public WasmTrapException()
        : base("The extraction module trapped.")
    {
    }
}
