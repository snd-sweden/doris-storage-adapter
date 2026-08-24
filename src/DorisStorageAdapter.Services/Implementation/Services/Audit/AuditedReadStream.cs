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
        _underlyingStream = underlyingStream;
        _auditHandle = auditHandle;
        _expectedLength = expectedLength;

        SetBytesRead();
    }

    public override bool CanRead => _underlyingStream.CanRead;
    public override bool CanSeek => _underlyingStream.CanSeek;
    public override bool CanTimeout => _underlyingStream.CanTimeout;
    public override bool CanWrite => false;

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

    public override int Read(
        byte[] buffer,
        int offset,
        int count)
    {
        try
        {
            int bytesRead = _underlyingStream.Read(
                buffer,
                offset,
                count);

            AddBytesRead(bytesRead);

            return bytesRead;
        }
        catch (Exception ex)
        {
            CompleteFromExceptionSynchronously(ex);
            throw;
        }
    }

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
        if (!_disposed)
        {
            if (disposing)
            {
                CompleteSynchronously(
                    GetDisposeOutcome());

                _underlyingStream.Dispose();
            }

            _disposed = true;
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await _auditHandle
                .CompleteAsync(GetDisposeOutcome())
                .ConfigureAwait(false);

            await _underlyingStream
                .DisposeAsync()
                .ConfigureAwait(false);

            _disposed = true;
        }

        await base
            .DisposeAsync()
            .ConfigureAwait(false);
    }

    public override void Flush() =>
        _underlyingStream.Flush();

    public override Task FlushAsync(
        CancellationToken cancellationToken) =>
        _underlyingStream.FlushAsync(cancellationToken);

    public override long Seek(
        long offset,
        SeekOrigin origin) =>
        _underlyingStream.Seek(offset, origin);

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
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(
            new NotSupportedException());
}