using Xunit;

namespace Verifiabl.Verifier.Extraction.Tests;

public class ExtractionWorkTests
{
    [Fact]
    public async Task ReturnsTheResultOfFinishedWork()
    {
        Assert.Equal(42, await ExtractionWork.RunAsync(_ => 42));
    }

    [Fact]
    public async Task StopsWaitingForWorkThatIgnoresCancellation()
    {
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // The work blocks without checking the token, as a long QR decode does.
        Task<int> waiting = ExtractionWork.RunAsync(
            _ =>
            {
                release.Wait(TimeSpan.FromSeconds(30));
                return 1;
            },
            cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        release.Set();
    }

    [Fact]
    public async Task PassesThroughFailures()
    {
        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => ExtractionWork.RunAsync<int>(
                _ => throw new PayloadExtractionException(PayloadExtractionFailure.Ambiguous, "two codes")));

        Assert.Equal(PayloadExtractionFailure.Ambiguous, error.Failure);
    }

    [Fact]
    public async Task AbandonedWorkKeepsItsSlotUntilItFinishes()
    {
        using var release = new ManualResetEventSlim();
        for (int run = 0; run < ExtractionWork.MaxConcurrency; run++)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
            Task<int> waiting = ExtractionWork.RunAsync(
                _ =>
                {
                    release.Wait(TimeSpan.FromSeconds(30));
                    return 1;
                },
                cancellation.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }

        // Every slot is held by abandoned work, so new work waits and times out.
        using (var full = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExtractionWork.RunAsync(_ => 2, full.Token));
        }

        release.Set();
        Assert.Equal(3, await ExtractionWork.RunAsync(_ => 3));
    }

    [Fact]
    public async Task RejectsNullWork()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => ExtractionWork.RunAsync<int>(null!));
    }
}
