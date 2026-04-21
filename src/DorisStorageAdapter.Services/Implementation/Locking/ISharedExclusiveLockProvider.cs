using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Locking;

internal interface ISharedExclusiveLockProvider
{
    ValueTask<IAsyncDisposable?> TryAcquireSharedLockAsync(string name, CancellationToken cancellationToken);

    ValueTask<IAsyncDisposable?> TryAcquireExclusiveLockAsync(string name, CancellationToken cancellationToken);
}
