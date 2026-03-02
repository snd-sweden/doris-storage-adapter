using DorisStorageAdapter.Services.Implementation.Lock;
using DorisStorageAdapter.Services.Implementation.Storage;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation;

internal sealed class StoragePathLock(ILockService lockService) : IStoragePathLock
{
    private readonly ILockService lockService = lockService;

    public ValueTask<IAsyncDisposable> LockPath(string path, CancellationToken cancellationToken) =>
        lockService.LockPath(path, cancellationToken);
}
