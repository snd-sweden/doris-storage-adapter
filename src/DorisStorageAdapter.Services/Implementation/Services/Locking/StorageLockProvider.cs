using DorisStorageAdapter.Services.Implementation.Locking;
using DorisStorageAdapter.Services.Implementation.Storage;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services.Locking;

internal sealed class StorageLockProvider(ILockProvider lockProvider) : IStorageLockProvider
{
    private readonly ILockProvider _lockProvider = lockProvider;

    public ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken) =>
        _lockProvider.AcquireAsync(LockKeys.Storage, cancellationToken);
}
