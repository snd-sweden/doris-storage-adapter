using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Common;

/// <summary>
/// Determines how <see cref="VirtualSeekableStream"/> handles attempts to
/// change the stream position.
/// </summary>
public enum VirtualSeekableStreamMode
{
    /// <summary>
    /// Allows seeking by updating only the virtual Position. The underlying
    /// stream is not repositioned.
    /// </summary>
    AllowVirtualSeek,

    /// <summary>
    /// Throws when a seek operation would make the virtual Position differ
    /// from the number of bytes actually consumed from the underlying stream.
    /// </summary>
    ThrowOnSeek
}

/// <summary>
/// A stream that wraps another stream and reports itself as seekable while
/// exposing a fixed logical length.
/// </summary>
/// <remarks>
/// In <see cref="VirtualSeekableStreamMode.AllowVirtualSeek"/> mode, seeking
/// only affects the virtual <see cref="Position"/> property and does not seek
/// the underlying stream.
///
/// In <see cref="VirtualSeekableStreamMode.ThrowOnSeek"/> mode, attempts to
/// change the virtual position throw. This is useful when a consuming API needs
/// a stream that reports <see cref="CanSeek"/> as <see langword="true"/>, but
/// the underlying stream must still be consumed strictly forward-only.
/// </remarks>
/// <param name="underlyingStream">The underlying stream to wrap.</param>
/// <param name="length">The exact logical length exposed by this stream.</param>
/// <param name="mode">How repositioning attempts should be handled.</param>
public sealed class VirtualSeekableStream : Stream
{
    private readonly Stream _underlyingStream;
    private readonly long _length;
    private readonly VirtualSeekableStreamMode _mode;
    private readonly bool _leaveOpen;
    private long _position;

    public VirtualSeekableStream(
        Stream underlyingStream, 
        long length, 
        VirtualSeekableStreamMode mode,
        bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(underlyingStream);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        _underlyingStream = underlyingStream;
        _length = length;
        _mode = mode;
        _leaveOpen = leaveOpen;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanTimeout => _underlyingStream.CanTimeout;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => SetPosition(value);
    }

    public override int ReadTimeout
    {
        get => _underlyingStream.ReadTimeout;
        set => _underlyingStream.ReadTimeout = value;
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing && !_leaveOpen)
            {
                _underlyingStream.Dispose();
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
            if (!_leaveOpen)
            {
                await _underlyingStream.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    public override void Flush() { }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        if (_position >= _length)
        {
            return 0;
        }

        int max = (int)Math.Min(buffer.Length, _length - _position);

        int bytesRead = _underlyingStream.Read(buffer[..max]);
        ThrowIfPrematureEndOfStream(bytesRead);

        _position += bytesRead;
        return bytesRead;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        if (_position >= _length)
        {
            return 0;
        }

        int max = (int)Math.Min(buffer.Length, _length - _position);

        int bytesRead = await _underlyingStream
            .ReadAsync(buffer[..max], cancellationToken)
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

    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unknown seek origin.")
        };

        SetPosition(target);
        return _position;
    }

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    private void SetPosition(long value)
    {
        if (value < 0 || value > _length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Position must be between 0 and the length of the stream.");
        }

        if (_mode == VirtualSeekableStreamMode.ThrowOnSeek && 
            value != _position)
        {
            throw new NotSupportedException(
                "This stream can not be repositioned.");
        }

        _position = value;
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
            $"Underlying stream ended after {_position} bytes, expected {_length} bytes.");
}
