
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Common;

/// <summary>
/// Base class for read-only streams that expose a fixed logical length.
/// </summary>
public abstract class FixedLengthStream : Stream
{
    private readonly Stream _underlyingStream;
    private readonly long _length;
    private readonly bool _leaveOpen;
    private long _position;
    private bool _disposed;

    protected FixedLengthStream(
        Stream underlyingStream,
        long length,
        bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(underlyingStream);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (!underlyingStream.CanRead)
        {
            throw new ArgumentException(
                "Stream must be readable.", nameof(underlyingStream));
        }

        _underlyingStream = underlyingStream;
        _length = length;
        _leaveOpen = leaveOpen;
    }

    protected Stream UnderlyingStream => _underlyingStream;

    protected bool IsDisposed => _disposed;

    public override bool CanRead =>
        !_disposed && _underlyingStream.CanRead;

    public override bool CanWrite => false;

    public override bool CanTimeout =>
        _underlyingStream.CanTimeout;

    public override int ReadTimeout
    {
        get => _underlyingStream.ReadTimeout;
        set => _underlyingStream.ReadTimeout = value;
    }

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        if (!CanSeek)
        {
            throw new NotSupportedException();
        }

        long position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(_position + offset),
            SeekOrigin.End => checked(_length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (position < 0 || position > _length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                "Cannot seek outside the stream.");
        }

        SeekToPosition(position);

        _position = position;
        return position;
    }

    protected abstract void SeekToPosition(long position);

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int count = (int)Math.Min(
            buffer.Length, _length - _position);

        if (count == 0)
        {
            return 0;
        }

        int bytesRead = _underlyingStream.Read(buffer[..count]);
        ThrowIfPrematureEndOfStream(bytesRead);

        _position += bytesRead;
        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        int count = (int)Math.Min(
            buffer.Length, _length - _position);

        if (count == 0)
        {
            return 0;
        }

        int bytesRead = await _underlyingStream
            .ReadAsync(buffer[..count], cancellationToken)
            .ConfigureAwait(false);

        ThrowIfPrematureEndOfStream(bytesRead);

        _position += bytesRead;
        return bytesRead;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        ReadAsync(
            buffer.AsMemory(offset, count),
            cancellationToken).AsTask();

    public override int ReadByte()
    {
        if (_position >= _length)
        {
            return -1;
        }

        int result = _underlyingStream.ReadByte();

        if (result < 0)
        {
            ThrowPrematureEndOfStream();
        }

        _position++;
        return result;
    }

    public override void Flush() { }

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(
        byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (!_disposed)
            {
                _disposed = true;

                if (disposing && !_leaveOpen)
                {
                    _underlyingStream.Dispose();
                }
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            if (!_disposed)
            {
                _disposed = true;

                if (!_leaveOpen)
                {
                    await _underlyingStream
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            GC.SuppressFinalize(this);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ThrowIfPrematureEndOfStream(int bytesRead)
    {
        if (bytesRead == 0 && _position < _length)
        {
            ThrowPrematureEndOfStream();
        }
    }

    private void ThrowPrematureEndOfStream() =>
        throw new EndOfStreamException(
            $"Underlying stream ended after {_position} bytes, " +
            $"expected {_length} bytes.");
}
