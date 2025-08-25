using DorisStorageAdapter.Services.Contract.Models;
using Medallion.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Lock;

internal class RedisLockService(
    IDistributedLockProvider lockProvider,
    IDistributedReaderWriterLockProvider readerWriterLockProvider) : ILockService
{
    private readonly IDistributedLockProvider lockProvider = lockProvider;
    private readonly IDistributedReaderWriterLockProvider readerWriterLockProvider = readerWriterLockProvider;

    public async Task<IDisposable> LockPath(string path, CancellationToken cancellationToken)
    {
        return await lockProvider.AcquireLockAsync(path, null, cancellationToken);
    }

    public async Task<bool> TryLockDatasetVersionExclusive(DatasetVersion datasetVersion, Func<Task> task, CancellationToken cancellationToken)
    {
        using var handle = await readerWriterLockProvider.TryAcquireWriteLockAsync(
            datasetVersion.Identifier + '-' + datasetVersion.Version, TimeSpan.Zero, cancellationToken);

        if (handle != null)
        {
            await task();
            return true;
        }

        return false;
    }

    public async Task<bool> TryLockDatasetVersionShared(DatasetVersion datasetVersion, Func<Task> task, CancellationToken cancellationToken)
    {
        using var handle = await readerWriterLockProvider.TryAcquireReadLockAsync(
            datasetVersion.Identifier + '-' + datasetVersion.Version, TimeSpan.Zero, cancellationToken);

        if (handle != null)
        {
            await task();
            return true;
        }

        return false;
    }

    public async Task<bool> TryLockPath(string path, Func<Task> task, CancellationToken cancellationToken)
    {
        using var handle = await lockProvider.TryAcquireLockAsync(path, TimeSpan.Zero, cancellationToken);

        if (handle != null)
        {
            await task();
            return true;
        }

        return false;
    }
}
