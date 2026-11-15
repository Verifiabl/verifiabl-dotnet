using Verifiabl.Verifier.Extraction.Internal;
using Xunit;

namespace Verifiabl.Verifier.Extraction.Tests;

public class ExtractionWorkTests
{
    // Only fails a wait for a signal that never comes, instead of hanging the run.
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    // Each test has its own slots, so other tests' reads cannot take or free one.
    private readonly ExtractionWork work = new(maxConcurrency: 2);

    [Fact]
    public async Task ReturnsTheResultOfFinishedWork()
    {
        Assert.Equal(42, await work.RunAsync(_ => 42));
    }

    [Fact]
    public async Task StopsWaitingForWorkThatIgnoresCancellation()
    {
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();

        Task<int> waiting = StartBlockedWork(release, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        release.Set();
    }

    [Fact]
    public async Task PassesThroughFailures()
    {
        PayloadExtractionException error = await Assert.ThrowsAsync<PayloadExtractionException>(
            () => work.RunAsync<int>(
                _ => throw new PayloadExtractionException(PayloadExtractionFailure.Ambiguous, "two codes")));

        Assert.Equal(PayloadExtractionFailure.Ambiguous, error.Failure);
    }

    [Fact]
    public async Task AbandonedWorkKeepsItsSlotUntilItFinishes()
    {
        using var release = new ManualResetEventSlim();
        for (int run = 0; run < work.MaxConcurrency; run++)
        {
            using var cancellation = new CancellationTokenSource();
            Task<int> waiting = StartBlockedWork(release, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }

        // Every slot is held by abandoned work, so new work cannot start.
        bool ran = false;
        using (var queued = new CancellationTokenSource())
        {
            Task<int> waiting = work.RunAsync(
                _ =>
                {
                    ran = true;
                    return 2;
                },
                queued.Token);
            queued.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }

        Assert.False(ran);
        release.Set();
        Assert.Equal(3, await work.RunAsync(_ => 3));
    }

    [Fact]
    public async Task RejectsNullWork()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => work.RunAsync<int>(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => work.RunOverCallerBufferAsync<int>(null!));
    }

    [Fact]
    public async Task ACancelledReadOfACallersBufferEndsOnlyWhenTheBufferIsNoLongerRead()
    {
        using var reading = new ManualResetEventSlim();
        using var finishCopy = new ManualResetEventSlim();
        using var finishRead = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();

        // The read copies the caller's buffer, says so, then decodes for a long time.
        Task<int> call = work.RunOverCallerBufferAsync(
            (_, copied) =>
            {
                reading.Set();
                finishCopy.Wait(HangGuard);
                copied();
                finishRead.Wait(HangGuard);
                return 1;
            },
            cancellation.Token);
        Assert.True(reading.Wait(HangGuard));

        cancellation.Cancel();
        Assert.NotSame(call, await Task.WhenAny(call, Task.Delay(TimeSpan.FromMilliseconds(200))));

        // The copy is over, so the call ends although the read goes on.
        finishCopy.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        finishRead.Set();
    }

    [Fact]
    public async Task AReadOfACallersBufferReturnsItsResult()
    {
        Assert.Equal(7, await work.RunOverCallerBufferAsync((_, copied) =>
        {
            copied();
            return 7;
        }));
    }

    [Fact]
    public async Task AReadThatBeginsAfterItsCallEndedDoesNotReadTheBuffer()
    {
        var buffer = new CallerBuffer();

        // The call ended before the read had a thread.
        await buffer.ReleaseAsync();

        Assert.False(buffer.TryBeginRead());
    }

    [Fact]
    public async Task ACallWaitsOnlyWhileTheBufferIsRead()
    {
        var buffer = new CallerBuffer();
        Assert.True(buffer.TryBeginRead());

        Task released = buffer.ReleaseAsync();
        Assert.False(released.IsCompleted);

        buffer.EndRead();
        await released;
        Assert.True(buffer.ReleaseAsync().IsCompleted);
    }

    // Starts work that holds its slot, blocked without checking the token as a long QR decode is, and returns once it runs.
    private Task<int> StartBlockedWork(ManualResetEventSlim release, CancellationToken cancellationToken)
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> waiting = work.RunAsync(
            _ =>
            {
                started.SetResult(true);
                release.Wait(HangGuard);
                return 1;
            },
            cancellationToken);
        Assert.True(started.Task.Wait(HangGuard));
        return waiting;
    }
}
