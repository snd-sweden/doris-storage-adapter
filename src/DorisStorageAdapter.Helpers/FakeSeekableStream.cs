using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Helpers;

/// <summary>
/// A stream that wraps another stream to make it appear as though the underlying
/// stream is seekable. Seeking only affects the Position property and does not
/// actually seek in the underlying stream.
/// </summary>
/// <param name="underlyingStream">The underlying stream to wrap.</param>
/// <param name="length">The length of the underlying stream.</param>
public sealed class FakeSeekableStream(Stream underlyingStream, long length) : Stream
{
    private readonly Stream _underlyingStream = underlyingStream;
    private readonly long _length = length;
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanTimeout => _underlyingStream.CanTimeout;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set
        {
            if (value < 0 || value > _length)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, 
                    "Position must be between 0 and the length of the stream.");
            }
            _position = value;
        }
    }

    public override int ReadTimeout
    {
        get => _underlyingStream.ReadTimeout;
        set => _underlyingStream.ReadTimeout = value;
    }

    public override void Close()
    {
        try
        {
            _underlyingStream.Close();
        }
        finally
        {
            base.Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                _underlyingStream.Dispose();
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    public async override ValueTask DisposeAsync()
    {
        try
        {
            await _underlyingStream.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    public override void Flush() => throw new NotSupportedException();

    public override int Read(Span<byte> buffer)
    {
        int bytesRead = _underlyingStream.Read(buffer);
        _position += bytesRead;
        return bytesRead;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int bytesRead = _underlyingStream.Read(buffer, offset, count);
        _position += bytesRead;
        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.IsEmpty || Position >= Length)
        {
            return 0;
        }

        int bytesRead = await _underlyingStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        _position += bytesRead;
        return bytesRead;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory().Slice(offset, count), cancellationToken).AsTask();

    public override int ReadByte()
    {
        var result = _underlyingStream.ReadByte();

        if (result > 0)
        {
            _position++;
        }

        return result;
    }

    public override long Seek(long offset, SeekOrigin origin) =>
         Position = origin switch
         {
             SeekOrigin.Begin => offset,
             SeekOrigin.Current => Position + offset,
             SeekOrigin.End => _length + offset,
             _ => throw new NotSupportedException()
         };

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}