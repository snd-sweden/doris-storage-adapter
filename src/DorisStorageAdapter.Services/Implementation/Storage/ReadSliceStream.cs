
using DorisStorageAdapter.Common;
using System;
using System.IO;

namespace DorisStorageAdapter.Services.Implementation.Storage;

/// <summary>
/// A read-only stream that exposes a fixed-length slice of another stream.
/// Supports seeking within the slice when the underlying stream is seekable.
/// </summary>
internal sealed class ReadSliceStream : FixedLengthStream
{
    private readonly long _startPosition;

    public ReadSliceStream(
        Stream underlyingStream,
        long length,
        bool leaveOpen = false)
        : base(underlyingStream, length, leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(underlyingStream);

        _startPosition = underlyingStream.CanSeek
            ? underlyingStream.Position
            : 0;
    }

    public override bool CanSeek =>
        !IsDisposed && UnderlyingStream.CanSeek;

    protected override void SeekToPosition(long position)
    {
        UnderlyingStream.Seek(
            checked(_startPosition + position),
            SeekOrigin.Begin);
    }
}
