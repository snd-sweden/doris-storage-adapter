using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.Locking;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services.Locking;

internal sealed class DatasetVersionLocks(IReaderWriterLockProvider lockProvider)
{
    private readonly IReaderWriterLockProvider _lockProvider = lockProvider;

    public async ValueTask<IAsyncDisposable> AcquireReadLockOrThrowAsync(
        DatasetVersion datasetVersion,
        CancellationToken cancellationToken)
    {
        return await _lockProvider.TryAcquireReadLockAsync(
            LockKeys.DatasetVersion(datasetVersion),
            cancellationToken) ?? throw new ConflictException();
    }

    public async ValueTask<IAsyncDisposable> AcquireWriteLockOrThrowAsync(
        DatasetVersion datasetVersion,
        CancellationToken cancellationToken)
    {
       return await _lockProvider.TryAcquireWriteLockAsync(
            LockKeys.DatasetVersion(datasetVersion),
            cancellationToken) ?? throw new ConflictException();
    }
}
