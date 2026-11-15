using System.Runtime.ExceptionServices;
using System.Text.Json;
using Verifiabl.Verifier.Extraction.Internal;
using Verifiabl.Verifier.Extraction.Internal.Wasmtime;
using Xunit;

namespace Verifiabl.Verifier.Extraction.Tests;

/// <summary>Runs <see cref="StandInModules"/> in place of the extraction module.</summary>
[Collection(Name)]
public class StandInModuleTests
{
    internal const string Name = "Stand-in modules";

    private const string TimedOut = "timedOut";

    // A stand-in expected to spin is cancelled after this. It cannot finish, so the moment does not change its status.
    private static readonly TimeSpan SpinBudget = TimeSpan.FromMilliseconds(100);

    // Fails a stand-in that spins when it should finish, instead of hanging the run. Finishing ones take milliseconds.
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    // Small enough for a stand-in's one page of memory.
    private static readonly byte[] SmallFile =
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "ExtractionFixtures", "not-a-file.bin"));

    public static TheoryData<string, string> Expectations()
    {
        var expectations = new TheoryData<string, string>();
        using JsonDocument expected = JsonDocument.Parse(File.ReadAllText(StandInModules.ExpectationsFile));
        foreach (JsonElement standIn in expected.RootElement.EnumerateArray())
        {
            expectations.Add(standIn.GetProperty("module").GetString()!, standIn.GetProperty("status").GetString()!);
        }

        return expectations;
    }

    [Theory]
    [MemberData(nameof(Expectations))]
    public void ReportsEachStandInAsExpected(string module, string status)
    {
        WasmModule standIn = StandInModules.Get(module);
        TimeSpan budget = status == TimedOut ? SpinBudget : HangGuard;

        Assert.Equal(status, Status(budget, token => ExtractionModule.ReadImage(standIn, SmallFile, ExtractionModule.MaxMemoryBytes, token)));
        Assert.Equal(status, Status(budget, token => ExtractionModule.ReadPdf(standIn, SmallFile, token)));
        Assert.Equal(status, Status(budget, token => ReadAsPixels(standIn, token, pixelsCopied: null)));
    }

    [Fact]
    public void StopsARunningReadWhenItsTokenIsCancelled()
    {
        using var cancellation = new CancellationTokenSource();

        // Cancelled once the input is copied, after the read's own checks and before the module spins.
        Assert.ThrowsAny<OperationCanceledException>(
            () => ReadAsPixels(StandInModules.Spins, cancellation.Token, pixelsCopied: cancellation.Cancel));
    }

    [Fact]
    public void ReportsRunawayRecursionOnAThreadWithLittleStack()
    {
        WasmModule recurses = StandInModules.Get("recurses.wasm");

        // Less stack than the module may use before it traps.
        string status = OnThread(
            256 * 1024,
            () => Status(HangGuard, token => ExtractionModule.ReadImage(recurses, SmallFile, ExtractionModule.MaxMemoryBytes, token)));

        Assert.Equal("unreadable", status);
    }

    [Fact]
    public void RefusesAnOversizedResultWithoutCopyingIt()
    {
        WasmModule oversized = StandInModules.Get("too-large-result.wasm");
        long before = AllocatedBytes();

        string status = Status(HangGuard, token => ExtractionModule.ReadImage(oversized, SmallFile, ExtractionModule.MaxMemoryBytes, token));

        // The result is 64 MiB of valid JSON, so a host that copied it out would allocate at least that.
        long allocated = AllocatedBytes() - before;
        Assert.Equal("unreadable", status);
        Assert.True(allocated < 8 * 1024 * 1024, $"Reading the stand-in allocated {allocated} bytes.");
    }

    // The small file as one row of greyscale pixels.
    private static IReadOnlyList<string> ReadAsPixels(WasmModule standIn, CancellationToken cancellationToken, Action? pixelsCopied) =>
        ExtractionModule.ReadPixels(
            standIn,
            SmallFile,
            SmallFile.Length,
            1,
            PixelFormat.Gray8,
            ExtractionModule.MaxMemoryBytes,
            cancellationToken,
            pixelsCopied);

    /// <summary>The status of a read whose token is cancelled after <paramref name="budget"/>.</summary>
    private static string Status(TimeSpan budget, Action<CancellationToken> read)
    {
        using var cancellation = new CancellationTokenSource(budget);
        return ReadStatus.Of(() => read(cancellation.Token));
    }

    private static T OnThread<T>(int stackBytes, Func<T> work)
    {
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    result = work();
                }
                catch (Exception exception)
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
            },
            stackBytes);
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result;
    }

    private static long AllocatedBytes()
    {
#if NETFRAMEWORK
        AppDomain.MonitoringIsEnabled = true;
        return AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
#else
        return GC.GetTotalAllocatedBytes(precise: true);
#endif
    }
}

/// <summary>Not run beside other tests: one test measures what the whole process allocates.</summary>
[CollectionDefinition(StandInModuleTests.Name, DisableParallelization = true)]
public sealed class StandInModuleCollection
{
}
