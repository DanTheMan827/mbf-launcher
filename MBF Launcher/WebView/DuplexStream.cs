namespace MBF_Launcher.WebView
{
    /// <summary>
    /// A <see cref="Stream"/> that reads from one underlying stream and writes to
    /// another.  Used to combine two unidirectional
    /// <see cref="System.IO.Pipelines.Pipe"/> streams into a single bidirectional
    /// stream for <see cref="VirtualAdbServer"/>.
    /// </summary>
    internal sealed class DuplexStream : Stream
    {
        private readonly Stream _reader;
        private readonly Stream _writer;

        public DuplexStream(Stream reader, Stream writer)
        {
            _reader = reader;
            _writer = writer;
        }

        public override bool CanRead  => true;
        public override bool CanSeek  => false;
        public override bool CanWrite => true;
        public override long Length   => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _reader.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            _reader.ReadAsync(buffer, offset, count, ct);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            _reader.ReadAsync(buffer, ct);

        public override void Write(byte[] buffer, int offset, int count) =>
            _writer.Write(buffer, offset, count);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            _writer.WriteAsync(buffer, offset, count, ct);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) =>
            _writer.WriteAsync(buffer, ct);

        public override void Flush() => _writer.Flush();

        public override Task FlushAsync(CancellationToken ct) => _writer.FlushAsync(ct);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _reader.Dispose();
                _writer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
