using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Lock;

internal interface IReaderWriterLockProvider
{
    ValueTask<IAsyncDisposable?> TryAcquireReadLockAsync(string name, CancellationToken cancellationToken);

    ValueTask<IAsyncDisposable?> TryAcquireWriteLockAsync(string name, CancellationToken cancellationToken);
}
