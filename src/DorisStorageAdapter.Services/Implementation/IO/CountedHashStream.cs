using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.IO;

/// <summary>
/// A stream that wraps another stream and calculates the number of bytes read,
/// and the SHA256 hash, of the bytes read from the underlying stream.
/// </summary>
/// <param name="underlyingStream">The underlying stream to wrap.</param>
internal sealed class CountedHashStream : Stream
{
    private readonly Stream _underlyingStream;
    private long _bytesRead;
    private readonly IncrementalHash _sha256hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private byte[] _hashValue = [];
    private bool _isDisposed;

    public CountedHashStream(Stream underlyingStream)
    {
        ArgumentNullException.ThrowIfNull(underlyingStream);

        _underlyingStream = underlyingStream;
    }

    public long BytesRead => _bytesRead;

    public byte[] GetHash() => _isDisposed 
        ? [.. _hashValue]
        : _sha256hasher.GetCurrentHash();

    public override bool CanRead => _underlyingStream.CanRead;
    public override bool CanSeek => _underlyingStream.CanSeek;
    public override bool CanTimeout => _underlyingStream.CanTimeout;
    public override bool CanWrite => _underlyingStream.CanWrite;
    public override long Length => _underlyingStream.Length;

    public override long Position
    {
        get => _underlyingStream.Position;
        set => _underlyingStream.Position = value;
    }

    public override int ReadTimeout
    {
        get => _underlyingStream.ReadTimeout;
        set => _underlyingStream.ReadTimeout = value;
    }

    public override int WriteTimeout
    {
        get => _underlyingStream.WriteTimeout;
        set => _underlyingStream.WriteTimeout = value;
    }

    private void DisposeSha256Hasher()
    {
        _hashValue = _sha256hasher.GetCurrentHash();
        _sha256hasher.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing && !_isDisposed)
            {
                DisposeSha256Hasher();
                _isDisposed = true;

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
            if (!_isDisposed)
            {
                DisposeSha256Hasher();
                _isDisposed = true;

                await _underlyingStream.DisposeAsync()
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    public override void Flush() => 
        _underlyingStream.Flush();

    public override Task FlushAsync(CancellationToken cancellation) => 
        _underlyingStream.FlushAsync(cancellation);

    public override int Read(Span<byte> buffer)
    {
        int bytesRead = _underlyingStream.Read(buffer);

        _bytesRead += bytesRead;
        _sha256hasher.AppendData(buffer[..bytesRead]);

        return bytesRead;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int bytesRead = await _underlyingStream
            .ReadAsync(buffer, cancellationToken)
            .ConfigureAwait(false);

        _bytesRead += bytesRead;
        _sha256hasher.AppendData(buffer[..bytesRead].Span);

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
        var result = _underlyingStream.ReadByte();

        if (result >= 0)
        {
            _bytesRead++;
            _sha256hasher.AppendData([(byte)result]);
        }

        return result;
    }

    public override long Seek(long offset, SeekOrigin origin) => 
        _underlyingStream.Seek(offset, origin);

    public override void SetLength(long value) => 
        _underlyingStream.SetLength(value);

    public override void Write(ReadOnlySpan<byte> buffer) => 
        _underlyingStream.Write(buffer);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) =>
        _underlyingStream.WriteAsync(buffer, cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => 
        _underlyingStream.Write(buffer, offset, count);

    public override Task WriteAsync(
        byte[] buffer, 
        int offset, 
        int count, 
        CancellationToken cancellationToken) =>
        _underlyingStream.WriteAsync(buffer, offset, count, cancellationToken);

    public override void WriteByte(byte value) => 
        _underlyingStream.WriteByte(value);
}