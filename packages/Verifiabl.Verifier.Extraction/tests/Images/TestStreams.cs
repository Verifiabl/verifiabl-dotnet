namespace Verifiabl.Verifier.Extraction.Images.Tests;

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

    /// <summary>Returns zero bytes forever and counts how many it handed out.</summary>
    internal sealed class Endless : ReadOnlyStream
    {
        internal long BytesRead { get; private set; }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            BytesRead += count;
            return Task.FromResult(count);
        }
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
