using System.Runtime.InteropServices;

namespace Verifiabl.Verifier.Extraction.Internal.Wasmtime;

/// <summary>
/// The parts of Wasmtime's C API the image sandbox uses. Layouts follow the
/// headers of the release pinned in <c>Wasmtime.props</c>; check them when its
/// major version changes.
/// </summary>
/// <remarks>
/// Wasmtime's .NET wrapper is not strong-named, so a signed .NET Framework
/// assembly cannot reference it. This binds the native library instead.
/// </remarks>
internal static class WasmtimeNative
{
    internal const string Library = "wasmtime";

    /// <summary><c>WASMTIME_EXTERN_FUNC</c>.</summary>
    internal const byte ExternFunc = 0;

    /// <summary><c>WASMTIME_EXTERN_MEMORY</c>.</summary>
    internal const byte ExternMemory = 3;

    /// <summary><c>WASMTIME_UPDATE_DEADLINE_CONTINUE</c>.</summary>
    internal const byte UpdateDeadlineContinue = 0;

    /// <summary><c>WASM_I32</c>, a <c>wasm_valkind_t</c>.</summary>
    internal const byte ValueKindI32 = 0;

    /// <summary>
    /// Bytes reserved for a <c>wasmtime_extern_t</c>: a 1-byte kind, then a
    /// union aligned to 8 bytes. The largest union member is smaller than this.
    /// </summary>
    internal const int ExternBytes = 64;

    /// <summary>Offset of <c>wasmtime_extern_t.of</c>.</summary>
    internal const int ExternUnionOffset = 8;

    /// <summary>Called when a store reaches its epoch deadline.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr EpochDeadlineCallback(IntPtr context, IntPtr data, ref ulong deadlineDelta, ref byte updateKind);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wasm_config_new();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_config_epoch_interruption_set(IntPtr config, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_config_signals_based_traps_set(IntPtr config, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_config_max_wasm_stack_set(IntPtr config, UIntPtr bytes);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_config_wasm_threads_set(IntPtr config, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_config_wasm_gc_set(IntPtr config, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_config_wasm_memory64_set(IntPtr config, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_config_wasm_exceptions_set(IntPtr config, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_config_wasm_stack_switching_set(IntPtr config, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_config_wasm_component_model_set(IntPtr config, [MarshalAs(UnmanagedType.I1)] bool enable);

    /// <summary>Takes ownership of <paramref name="config"/>.</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wasm_engine_new_with_config(IntPtr config);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasm_engine_delete(IntPtr engine);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_engine_increment_epoch(IntPtr engine);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wasmtime_module_new(IntPtr engine, byte[] wasm, UIntPtr length, out IntPtr module);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_module_delete(IntPtr module);

    /// <summary>Fills <paramref name="exports"/>, which the caller frees with <see cref="wasm_exporttype_vec_delete"/>.</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_module_exports(IntPtr module, out PointerVector exports);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasm_exporttype_vec_delete(ref PointerVector exports);

    /// <summary>A <c>const wasm_name_t*</c> owned by the export type: a <see cref="ByteVector"/>, not null-terminated.</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wasm_exporttype_name(IntPtr exportType);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wasm_exporttype_type(IntPtr exportType);

    /// <summary>The function type, or zero when the export is not a function.</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wasm_externtype_as_functype_const(IntPtr externType);

    /// <summary>A <c>const wasm_valtype_vec_t*</c> owned by the function type: a <see cref="PointerVector"/>.</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wasm_functype_params(IntPtr functionType);

    /// <inheritdoc cref="wasm_functype_params"/>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wasm_functype_results(IntPtr functionType);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte wasm_valtype_kind(IntPtr valueType);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wasmtime_store_new(IntPtr engine, IntPtr data, IntPtr finalizer);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wasmtime_store_context(IntPtr store);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_store_limiter(IntPtr store, long memorySize, long tableElements, long instances, long tables, long memories);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_store_epoch_deadline_callback(IntPtr store, EpochDeadlineCallback callback, IntPtr data, IntPtr finalizer);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_context_set_epoch_deadline(IntPtr context, ulong ticksBeyondCurrent);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_store_delete(IntPtr store);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wasmtime_instance_new(IntPtr context, IntPtr module, IntPtr imports, UIntPtr importCount, out Handle instance, out IntPtr trap);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool wasmtime_instance_export_get(IntPtr context, ref Handle instance, byte[] name, UIntPtr nameLength, IntPtr item);

    /// <summary>
    /// Unchecked: Wasmtime reads every parameter from, and writes every result
    /// to, <paramref name="argumentsAndResults"/> without looking at its length
    /// or types. <see cref="WasmModule"/> checks each export's type when it
    /// compiles the module, and <see cref="WasmInstance.Call"/> the argument count.
    /// </summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wasmtime_func_call_unchecked(IntPtr context, ref Handle func, [In, Out] RawValue[] argumentsAndResults, UIntPtr length, out IntPtr trap);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wasmtime_memory_data(IntPtr context, ref Handle memory);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern UIntPtr wasmtime_memory_data_size(IntPtr context, ref Handle memory);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    internal static extern IntPtr wasmtime_error_new(string message);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_error_message(IntPtr error, out ByteVector message);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasmtime_error_delete(IntPtr error);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasm_trap_delete(IntPtr trap);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wasm_byte_vec_delete(ref ByteVector vector);

    /// <summary>
    /// An opaque handle large enough for each type it carries:
    /// <c>wasmtime_instance_t</c> and <c>wasmtime_func_t</c> are 16 bytes, and
    /// <c>wasmtime_memory_t</c> is 24 (a padded 12-byte struct, then 4 bytes).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Handle
    {
        internal ulong First;
        internal ulong Second;
        internal ulong Third;
    }

    /// <summary><c>wasmtime_val_raw_t</c>: 16 bytes, an <c>i32</c> in the first 4.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct RawValue
    {
        [FieldOffset(0)]
        internal int I32;
    }

    /// <summary><c>wasm_byte_vec_t</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ByteVector
    {
        internal UIntPtr Size;
        internal IntPtr Data;
    }

    /// <summary><c>wasm_exporttype_vec_t</c> and <c>wasm_valtype_vec_t</c>: a count and an array of pointers.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PointerVector
    {
        internal UIntPtr Size;
        internal IntPtr Data;
    }
}
