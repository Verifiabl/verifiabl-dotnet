namespace Verifiabl.Verifier.Extraction;

/// <summary>
/// Runs CPU-bound extraction, such as QR decoding, so a deadline or
/// cancellation ends the wait even when the work itself cannot be interrupted.
/// </summary>
/// <remarks>
/// At most <see cref="MaxConcurrency"/> extractions run at once in the process.
/// Work abandoned after a timeout keeps its slot until it finishes, so a burst
/// of timeouts cannot pile up unbounded CPU and memory use. Run only work over
/// buffers the SDK owns; await reads of caller-owned streams instead.
/// </remarks>
public static class ExtractionWork
{
    // Declared before Slots: static initializers run in textual order.

    /// <summary>How many extractions may run at once: the processor count.</summary>
    public static int MaxConcurrency { get; } = Math.Max(1, Environment.ProcessorCount);

    private static readonly SemaphoreSlim Slots = new(MaxConcurrency, MaxConcurrency);

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
    public static async Task<T> RunAsync<T>(Func<CancellationToken, T> extract, CancellationToken cancellationToken = default)
    {
        if (extract is null)
        {
            throw new ArgumentNullException(nameof(extract));
        }

        await Slots.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                        Slots.Release();
                    }
                },
                CancellationToken.None);
        }
        catch
        {
            Slots.Release();
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
}
