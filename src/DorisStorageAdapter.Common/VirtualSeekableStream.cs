
using System;
using System.IO;

namespace DorisStorageAdapter.Common;

/// <summary>
/// A fixed-length stream that reports itself as seekable without
/// necessarily supporting real seeking.
/// </summary>
public sealed class VirtualSeekableStream(
    Stream underlyingStream,
    long length,
    bool throwOnSeek,
    bool leaveOpen = false) : FixedLengthStream(underlyingStream, length, leaveOpen)
{
    private readonly bool _throwOnSeek = throwOnSeek;

    public override bool CanSeek => !IsDisposed;

    protected override void SeekToPosition(long position)
    {
        if (_throwOnSeek &&
            position != Position)
        {
            throw new NotSupportedException(
                "This stream cannot be repositioned.");
        }
    }
}