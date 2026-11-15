namespace Verifiabl.Verifier.Extraction.Internal;

/// <summary>
/// Runs a read in the sandbox on the thread pool, so a deadline or
/// cancellation ends the wait at once.
/// </summary>
/// <remarks>
/// At most <see cref="MaxConcurrency"/> reads run at once: each can use the
/// sandbox's whole memory cap. Work abandoned after a timeout keeps its slot
/// until it finishes, so a burst of timeouts cannot pile up unbounded CPU and
/// memory use. Abandoned work can outlive its caller, so
/// <see cref="RunAsync"/> is only for work over buffers the SDK owns. Use
/// <see cref="RunOverCallerBufferAsync"/> for an array the caller owns, and
/// await reads of caller-owned streams instead of running them here.
/// </remarks>
internal sealed class ExtractionWork
{
    private readonly SemaphoreSlim slots;

    /// <summary>Run at most <paramref name="maxConcurrency"/> reads at once.</summary>
    /// <remarks>Tests make their own, so their slots are not the process's.</remarks>
    internal ExtractionWork(int maxConcurrency)
    {
        MaxConcurrency = maxConcurrency;
        slots = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    /// <summary>The process's reads: one at a time for each processor.</summary>
    internal static ExtractionWork Shared { get; } = new(Math.Max(1, Environment.ProcessorCount));

    /// <summary>How many reads may run at once.</summary>
    internal int MaxConcurrency { get; }

    /// <summary>
    /// Run <paramref name="extract"/> on the thread pool and return its result, or
    /// throw <see cref="OperationCanceledException"/> as soon as
    /// <paramref name="cancellationToken"/> is cancelled, including while waiting for a slot.
    /// </summary>
    /// <remarks>
    /// <paramref name="extract"/> receives the same token and should check it
    /// between steps. An abandoned run's failure is observed, so it never
    /// surfaces as an unobserved task exception.
    /// </remarks>
    internal async Task<T> RunAsync<T>(Func<CancellationToken, T> extract, CancellationToken cancellationToken = default)
    {
        if (extract is null)
        {
            throw new ArgumentNullException(nameof(extract));
        }

        await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        Task<T> work;
        try
        {
            work = Task.Run(
                () =>
                {
                    try
                    {
                        return extract(cancellationToken);
                    }
                    finally
                    {
                        slots.Release();
                    }
                },
                CancellationToken.None);
        }
        catch
        {
            slots.Release();
            throw;
        }

        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
        {
            if (await Task.WhenAny(work, cancelled.Task).ConfigureAwait(false) != work)
            {
                _ = work.ContinueWith(
                    abandoned => abandoned.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                throw new OperationCanceledException(cancellationToken);
            }
        }

        return await work.ConfigureAwait(false);
    }

    /// <summary>
    /// Run a read of an array that the caller owns. The returned task does not
    /// complete while the read still reads the array, so the array is the
    /// caller's again when the task completes, whatever its outcome.
    /// </summary>
    /// <param name="read">
    /// Receives the token, and an action to call as soon as it will not read
    /// the array again.
    /// </param>
    /// <param name="cancellationToken">Ends the wait, as for <see cref="RunAsync"/>.</param>
    /// <remarks>
    /// A cancelled read is abandoned as in <see cref="RunAsync"/>, with two
    /// differences: one that has not begun never reads the array, and one that
    /// is reading it is awaited until it calls the action or ends.
    /// </remarks>
    internal async Task<T> RunOverCallerBufferAsync<T>(
        Func<CancellationToken, Action, T> read,
        CancellationToken cancellationToken = default)
    {
        if (read is null)
        {
            throw new ArgumentNullException(nameof(read));
        }

        var buffer = new CallerBuffer();
        try
        {
            return await RunAsync(token => ReadOnce(read, buffer, token), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await buffer.ReleaseAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static T ReadOnce<T>(Func<CancellationToken, Action, T> read, CallerBuffer buffer, CancellationToken cancellationToken)
    {
        if (!buffer.TryBeginRead())
        {
            // The call ended while this read waited for a thread.
            throw new OperationCanceledException(cancellationToken);
        }

        try
        {
            return read(cancellationToken, buffer.EndRead);
        }
        finally
        {
            buffer.EndRead();
        }
    }
}
