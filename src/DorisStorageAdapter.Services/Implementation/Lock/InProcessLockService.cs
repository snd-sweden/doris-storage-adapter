using DorisStorageAdapter.Services.Contract.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Lock;

internal sealed class InProcessLockService(
    ILockProvider lockProvider, 
    IReaderWriterLockProvider readerWriterLockProvider) : ILockService
{
    private readonly ILockProvider _lockProvider = lockProvider;
    private readonly IReaderWriterLockProvider _readerWriterLockProvider = readerWriterLockProvider;

    public ValueTask<IAsyncDisposable> LockPath(string path, CancellationToken cancellationToken)
    {
        return _lockProvider.AcquireAsync(path, cancellationToken);
    }

    public async ValueTask<bool> TryLockPath(
        string path,
        Func<Task> task,
        CancellationToken cancellationToken)
    {
        var handle = await _lockProvider.TryAcquireAsync(path, cancellationToken);

        if (handle is null)
        {
            return false;
        }

        await using (handle)
        {
            await task();
        }

        return true;
    }

    public async ValueTask<bool> TryLockDatasetVersionExclusive(
        DatasetVersion datasetVersion,
        Func<Task> task,
        CancellationToken cancellationToken)
    {
        var handle = await _readerWriterLockProvider.TryAcquireWriteLockAsync(
            "datasetVersion:" + datasetVersion.Identifier + "-" + datasetVersion.Version,
            cancellationToken);

        if (handle is null)
        {
            return false;
        }

        await using (handle)
        {
            await task();
        }

        return true;
    }

    public async ValueTask<bool> TryLockDatasetVersionShared(
        DatasetVersion datasetVersion,
        Func<Task> task,
        CancellationToken cancellationToken)
    {
        var handle = await _readerWriterLockProvider.TryAcquireReadLockAsync(
           "datasetVersion:" + datasetVersion.Identifier + "-" + datasetVersion.Version,
           cancellationToken);

        if (handle is null)
        {
            return false;
        }

        await using (handle)
        {
            await task();
        }

        return true;
    }
}
