using System.Diagnostics;
using Verifiabl.Verifier.Extraction.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Verifiabl.Verifier.Extraction.Tests;

public class SandboxModuleTests(ITestOutputHelper output)
{
    [Fact]
    public async Task LoadsTheModuleOnceForEveryCaller()
    {
        // The time goes to the test results, where a slow compilation in CI can be told from a slow read.
        var elapsed = Stopwatch.StartNew();
        await Task.WhenAll(SandboxModule.LoadAsync(), SandboxModule.LoadAsync());
        output.WriteLine($"The module was ready after {elapsed.ElapsedMilliseconds} ms.");

        Assert.True(SandboxModule.LoadAsync().IsCompleted);
        Assert.Empty(ExtractionModule.ReadImage(File.ReadAllBytes(Fixture("blank.webp")), CancellationToken.None));
    }

    [Fact]
    public async Task ACancelledTokenDoesNotEndAWaitThatIsAlreadyOver()
    {
        await SandboxModule.LoadAsync();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await SandboxModule.LoadAsync(cancelled.Token);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "ExtractionFixtures", name);
}
