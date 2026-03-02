using DorisStorageAdapter.Services.Contract.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Lock;

internal interface ILockService
{
    ValueTask<IAsyncDisposable> LockPath(
        string path,
        CancellationToken cancellationToken);

    ValueTask<bool> TryLockPath(
        string path,
        Func<Task> task,
        CancellationToken cancellationToken);

    ValueTask<bool> TryLockDatasetVersionExclusive(
        DatasetVersion datasetVersion,
        Func<Task> task,
        CancellationToken cancellationToken);

    ValueTask<bool> TryLockDatasetVersionShared(
        DatasetVersion datasetVersion,
        Func<Task> task,
        CancellationToken cancellationToken);
}
