using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.IO;

/// <summary>
/// A forward-only, read-only stream that wraps another stream and calculates 
/// the number of bytes read, and the SHA256 hash, of the bytes read from the 
/// underlying stream.
/// </summary>
/// <param name="underlyingStream">The underlying stream to wrap.</param>
internal sealed class CountedHashStream : Stream
{
    private readonly Stream _underlyingStream;
    private long _bytesRead;
    private readonly IncrementalHash _sha256hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private byte[]? _hashValue;
    private bool _disposed;
    private readonly bool _leaveOpen;

    public CountedHashStream(Stream underlyingStream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(underlyingStream);

        if (!underlyingStream.CanRead)
        {
            throw new ArgumentException(
                "The underlying stream must be readable.",
                nameof(underlyingStream));
        }

        _underlyingStream = underlyingStream;
        _leaveOpen = leaveOpen;
    }

    public long BytesRead => _bytesRead;

    public byte[] GetHash() => _disposed
        ? _hashValue is { } hash
            ? [.. hash]
            : throw new InvalidOperationException(
                "The SHA-256 hash could not be finalized.")
        : _sha256hasher.GetCurrentHash();

    public override bool CanRead =>
        !_disposed && _underlyingStream.CanRead;

    public override bool CanSeek => false;
    public override bool CanWrite => false;

    public override bool CanTimeout =>
        _underlyingStream.CanTimeout;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int ReadTimeout
    {
        get => _underlyingStream.ReadTimeout;
        set => _underlyingStream.ReadTimeout = value;
    }

    public override void Flush() { }

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
        throw new NotSupportedException();

    public override void Write(
        byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    private void DisposeSha256Hasher()
    {
        try
        {
            _hashValue = _sha256hasher.GetCurrentHash();
        }
        finally
        {
            _sha256hasher.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing && !_disposed)
            {
                _disposed = true;

                try
                {
                    DisposeSha256Hasher();
                }
                finally
                {
                    if (!_leaveOpen)
                    {
                        _underlyingStream.Dispose();
                    }
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

                try
                {
                    DisposeSha256Hasher();
                }
                finally
                {
                    if (!_leaveOpen)
                    {
                        await _underlyingStream.DisposeAsync()
                            .ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}