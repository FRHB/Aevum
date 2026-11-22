namespace PhotoAevumStudio.Web.Services;

/// <summary>
/// Read-only stream wrapper that reports cumulative bytes read while streaming upload content.
/// Leaves the inner stream open on disposal so the caller retains ownership.
/// </summary>
internal sealed class ProgressReportingStream : Stream
{
    private readonly Stream _inner;
    private readonly Action<long> _onBytesRead;
    private long _totalBytesRead;

    public ProgressReportingStream(Stream inner, Action<long> onBytesRead)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _onBytesRead = onBytesRead ?? throw new ArgumentNullException(nameof(onBytesRead));
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken)
        => _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        RecordRead(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = _inner.Read(buffer);
        RecordRead(read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        RecordRead(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        RecordRead(read);
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var pos = _inner.Seek(offset, origin);
        if (pos == 0)
        {
            Interlocked.Exchange(ref _totalBytesRead, 0);
        }
        return pos;
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private void RecordRead(int bytesRead)
    {
        if (bytesRead <= 0) return;

        var total = Interlocked.Add(ref _totalBytesRead, bytesRead);
        _onBytesRead(total);
    }
}
