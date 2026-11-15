namespace Verifiabl.Verifier.Extraction.Internal;

/// <summary>
/// Tracks whether a read still reads an array that the caller owns, so the
/// call that started the read does not end while it does.
/// </summary>
/// <remarks>
/// <see cref="ExtractionWork.RunAsync"/> stops waiting for a read as soon as
/// its token is cancelled, and the read ends later. A read copies the caller's
/// array into the sandbox once, at its start. The call must wait while that
/// copy runs, and a read that has not begun must never start it.
/// </remarks>
internal sealed class CallerBuffer
{
    private const int Unread = 0;
    private const int Reading = 1;
    private const int Released = 2;

    private readonly TaskCompletionSource<bool> released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int state;

    /// <summary>The read calls this before it first reads the array.</summary>
    /// <returns>False when the call has ended, so the read must not read the array.</returns>
    internal bool TryBeginRead() =>
        Interlocked.CompareExchange(ref state, Reading, Unread) == Unread;

    /// <summary>The read calls this when it will not read the array again. More than one call is harmless.</summary>
    internal void EndRead()
    {
        Interlocked.Exchange(ref state, Released);
        released.TrySetResult(true);
    }

    /// <summary>The call awaits this before it ends without the read's result.</summary>
    /// <returns>A task that completes when no read reads the array, and none will.</returns>
    internal Task ReleaseAsync() =>
        Interlocked.CompareExchange(ref state, Released, Unread) == Reading ? released.Task : Task.CompletedTask;
}
