using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services.Audit;

internal sealed class AuditedReadStream : Stream
{
    private readonly Stream _underlyingStream;
    private readonly AuditExecutionHandle _auditHandle;
    private readonly long _expectedLength;

    private long _totalBytesRead;
    private bool _disposed;

    public AuditedReadStream(
        Stream underlyingStream,
        AuditExecutionHandle auditHandle,
        long expectedLength)
    {
        ArgumentNullException.ThrowIfNull(underlyingStream);
        ArgumentNullException.ThrowIfNull(auditHandle);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedLength);

        if (!underlyingStream.CanRead)
        {
            throw new ArgumentException(
                "The underlying stream must be readable.",
                nameof(underlyingStream));
        }

        _underlyingStream = underlyingStream;
        _auditHandle = auditHandle;
        _expectedLength = expectedLength;

        SetBytesRead();
    }

    public override bool CanRead =>
        !_disposed && _underlyingStream.CanRead;

    public override bool CanSeek => false;
    public override bool CanWrite => false;

    public override bool CanTimeout => _underlyingStream.CanTimeout;

    public override long Length =>
        throw new NotSupportedException();

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

    public override int Read(
        byte[] buffer,
        int offset,
        int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        try
        {
            int bytesRead = _underlyingStream.Read(buffer);

            AddBytesRead(bytesRead);

            return bytesRead;
        }
        catch (Exception ex)
        {
            CompleteFromExceptionSynchronously(ex);
            throw;
        }
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        try
        {
            int bytesRead = await _underlyingStream
                .ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);

            AddBytesRead(bytesRead);

            return bytesRead;
        }
        catch (Exception ex)
        {
            await _auditHandle
                .CompleteAsync(
                    AuditOutcomeMapper.FromException(
                        ex,
                        cancellationToken))
                .ConfigureAwait(false);

            throw;
        }
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        ReadAsync(
            buffer.AsMemory(offset, count),
            cancellationToken).AsTask();

    private void AddBytesRead(int bytesRead)
    {
        if (bytesRead <= 0)
        {
            return;
        }

        _totalBytesRead += bytesRead;

        SetBytesRead();
    }

    private void SetBytesRead()
    {
        _auditHandle.State.Details["BytesRead"] =
            _totalBytesRead;
    }

    private AuditOutcome GetDisposeOutcome() =>
        _totalBytesRead >= _expectedLength
            ? AuditOutcome.Success
            : AuditOutcome.Cancelled;

    private void CompleteFromExceptionSynchronously(
        Exception exception)
    {
        CompleteSynchronously(
            AuditOutcomeMapper.FromException(
                exception,
                CancellationToken.None));
    }

    private void CompleteSynchronously(
        AuditOutcome outcome)
    {
        _auditHandle
            .CompleteAsync(outcome)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (!_disposed)
            {
                _disposed = true;

                if (disposing)
                {
                    try
                    {
                        CompleteSynchronously(GetDisposeOutcome());
                    }
                    finally
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
                    await _auditHandle
                        .CompleteAsync(GetDisposeOutcome())
                        .ConfigureAwait(false);
                }
                finally
                {
                    await _underlyingStream
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(
        byte[] buffer,
        int offset,
        int count) =>
        throw new NotSupportedException();

    public override void Write(
        ReadOnlySpan<byte> buffer) =>
        throw new NotSupportedException();

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken) =>
        ValueTask.FromException(
            new NotSupportedException());
}