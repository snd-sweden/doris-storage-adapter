using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.Locking;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services.Locking;

internal sealed class DatasetVersionLocks(ISharedExclusiveLockProvider lockProvider)
{
    private readonly ISharedExclusiveLockProvider _lockProvider = lockProvider;

    public async ValueTask<IAsyncDisposable> AcquireSharedLockOrThrowAsync(
        DatasetVersion datasetVersion,
        CancellationToken cancellationToken)
    {
        return await _lockProvider.TryAcquireSharedLockAsync(
            LockKeys.DatasetVersion(datasetVersion),
            cancellationToken) ?? throw new ConflictException();
    }

    public async ValueTask<IAsyncDisposable> AcquireExclusiveLockOrThrowAsync(
        DatasetVersion datasetVersion,
        CancellationToken cancellationToken)
    {
        return await _lockProvider.TryAcquireExclusiveLockAsync(
            LockKeys.DatasetVersion(datasetVersion),
            cancellationToken) ?? throw new ConflictException();
    }
}
