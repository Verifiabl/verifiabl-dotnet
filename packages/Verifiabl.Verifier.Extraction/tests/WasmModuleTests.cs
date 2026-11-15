using Verifiabl.Verifier.Extraction.Internal;
using Verifiabl.Verifier.Extraction.Internal.Wasmtime;
using Xunit;

namespace Verifiabl.Verifier.Extraction.Tests;

/// <summary>
/// Calls into the module are unchecked, so the host must refuse an export whose
/// type it has not checked rather than call it.
/// </summary>
public class WasmModuleTests
{
    private static readonly byte[] Header = [0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00];

    [Fact]
    public void RefusesAModuleWithoutAnExportTheHostCalls()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => ExtractionModule.Compile(Header));

        Assert.Contains("vf_abi_version", failure.Message);
    }

    [Fact]
    public void RefusesAnExportOfAnotherType()
    {
        // (func (export "vf_abi_version") (param i32) (result i32) (i32.const 1)): one parameter too many.
        byte[] module =
        [
            .. Header,
            0x01, 0x06, 0x01, 0x60, 0x01, 0x7F, 0x01, 0x7F,
            0x03, 0x02, 0x01, 0x00,
            0x07, 0x12, 0x01, 0x0E, .. "vf_abi_version"u8, 0x00, 0x00,
            0x0A, 0x06, 0x01, 0x04, 0x00, 0x41, 0x01, 0x0B,
        ];

        var failure = Assert.Throws<InvalidOperationException>(() => ExtractionModule.Compile(module));

        Assert.Contains("vf_abi_version does not take 0 32-bit integers", failure.Message);
    }

    [Fact]
    public void RefusesACallWithAnotherNumberOfArguments()
    {
        byte[] standIn = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "StandIns", "unknown-status.wasm"));
        using var instance = new WasmInstance(ExtractionModule.Compile(standIn), ExtractionModule.MaxMemoryBytes, CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => instance.Call("vf_alloc"));
        Assert.Throws<InvalidOperationException>(() => instance.Call("vf_read_image", 1024, 0));
        Assert.Throws<InvalidOperationException>(() => instance.Call("memory"));
        Assert.Equal(1024, instance.Call("vf_alloc", 16));
    }
}
