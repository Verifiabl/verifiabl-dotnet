using System.Collections.Concurrent;
using Verifiabl.Verifier.Extraction.Internal;
using Verifiabl.Verifier.Extraction.Internal.Wasmtime;

namespace Verifiabl.Verifier.Extraction.Tests;

/// <summary>
/// The extraction module's stand-ins, which misbehave as a decoder taken over
/// by a hostile file could. StandIns is a copy of the module's; do not edit it.
/// </summary>
internal static class StandInModules
{
    private static readonly string Folder = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "StandIns";

    /// <summary>The status the extraction module's tests expect from each stand-in.</summary>
    internal static readonly string ExpectationsFile = PathOf("expected.json");

    private static readonly ConcurrentDictionary<string, WasmModule> Compiled = new();

    /// <summary>Spins forever in every read, so only cancellation ends it.</summary>
    internal static WasmModule Spins => Get("spins.wasm");

    /// <summary>The stand-in compiled from <paramref name="module"/>, compiled once per process.</summary>
    internal static WasmModule Get(string module) =>
        Compiled.GetOrAdd(module, name => ExtractionModule.Compile(File.ReadAllBytes(PathOf(name))));

    // Only the file's name is used, so an entry in expected.json cannot name a file elsewhere.
    private static string PathOf(string module) => Folder + Path.DirectorySeparatorChar + Path.GetFileName(module);
}
