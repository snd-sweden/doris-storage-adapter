using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Lock.InProcess;

internal sealed class InProcessReaderWriterLockProvider : IReaderWriterLockProvider
{
    private static readonly CancellationToken _alreadyCanceled = new(canceled: true);

    private readonly ConcurrentDictionary<string, AsyncReaderWriterLock> _locks =
        new(StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable?> TryAcquireReadLockAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();

        var rwLock = _locks.GetOrAdd(name, static _ => new AsyncReaderWriterLock());

        try
        {
            // Since we pass an already cancelled token, ReaderLockAsync will throw
            // OperationCanceledException unless the lock can be granted immediately.
            var releaser = await rwLock.ReaderLockAsync(_alreadyCanceled).ConfigureAwait(false);
            return new DisposableAsAsyncDisposable(releaser);
        }
        catch (OperationCanceledException)
        {
            // Not acquired immediately.
            return null;
        }
    }

    public async ValueTask<IAsyncDisposable?> TryAcquireWriteLockAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();

        var rwLock = _locks.GetOrAdd(name, static _ => new AsyncReaderWriterLock());

        try
        {
            // Since we pass an already cancelled token, WriterLockAsync will throw
            // OperationCanceledException unless the lock can be granted immediately.
            var releaser = await rwLock.WriterLockAsync(_alreadyCanceled).ConfigureAwait(false);
            return new DisposableAsAsyncDisposable(releaser);
        }
        catch (OperationCanceledException)
        {
            // Not acquired immediately.
            return null;
        }
    }
}