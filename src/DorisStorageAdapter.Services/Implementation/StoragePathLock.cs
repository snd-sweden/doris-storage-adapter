using DorisStorageAdapter.Services.Implementation.Lock;
using DorisStorageAdapter.Services.Implementation.Storage;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation;

internal sealed class StoragePathLock(ILockProvider lockProvider) : IStoragePathLock
{
    private readonly ILockProvider _lockProvider = lockProvider;

    public ValueTask<IAsyncDisposable> LockPath(string path, CancellationToken cancellationToken) =>
        _lockProvider.AcquireAsync(path, cancellationToken);
}
