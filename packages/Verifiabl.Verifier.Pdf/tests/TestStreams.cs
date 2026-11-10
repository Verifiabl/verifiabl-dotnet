namespace Verifiabl.Verifier.Pdf.Tests;

/// <summary>Read-only streams that misbehave in the ways uploads can.</summary>
internal static class TestStreams
{
    /// <summary>Never returns data, but honours cancellation.</summary>
    internal sealed class Stalled : ReadOnlyStream
    {
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }
    }

    /// <summary>
    /// Ignores cancellation: its read keeps going for <c>lingerAfterCancel</c>
    /// after the token is cancelled, then returns end of stream.
    /// </summary>
    internal sealed class IgnoresCancellation : ReadOnlyStream
    {
        private readonly TimeSpan _lingerAfterCancel;

        internal IgnoresCancellation(TimeSpan lingerAfterCancel)
        {
            _lingerAfterCancel = lingerAfterCancel;
        }

        internal bool ReadFinished { get; private set; }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await UntilCancelledAsync(cancellationToken).ConfigureAwait(false);
            await Task.Delay(_lingerAfterCancel, CancellationToken.None).ConfigureAwait(false);
            ReadFinished = true;
            return 0;
        }
    }

    /// <summary>
    /// Returns one byte once the token is cancelled, ignoring the cancellation.
    /// A second read stalls for seconds, then ends the stream, so a reader that
    /// starts one fails its test instead of hanging it.
    /// </summary>
    internal sealed class LateChunkThenStalls : ReadOnlyStream
    {
        internal int Reads { get; private set; }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Reads++;
            if (Reads > 1)
            {
                await Task.Delay(GiveUpAfter, CancellationToken.None).ConfigureAwait(false);
                return 0;
            }

            await UntilCancelledAsync(cancellationToken).ConfigureAwait(false);
            buffer[offset] = (byte)'%';
            return 1;
        }
    }

    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Wait for cancellation instead of a fixed delay: on a busy runner a short
    /// deadline timer can fire after a longer read delay, which made the read
    /// finish before the deadline. Gives up after a few seconds so a reader
    /// that never cancels fails its test instead of hanging it.
    /// </summary>
    private static async Task UntilCancelledAsync(CancellationToken cancellationToken)
    {
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
        {
            await Task.WhenAny(cancelled.Task, Task.Delay(GiveUpAfter)).ConfigureAwait(false);
        }
    }

    /// <summary>Fails every read with its own cancellation, as a closed network stream can.</summary>
    internal sealed class CancelsItself : ReadOnlyStream
    {
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromCanceled<int>(new CancellationToken(canceled: true));
    }

    internal abstract class ReadOnlyStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
