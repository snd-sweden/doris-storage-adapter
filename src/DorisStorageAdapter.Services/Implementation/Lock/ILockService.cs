using DorisStorageAdapter.Services.Contract.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Lock;

internal interface ILockService
{
    Task<IDisposable> LockPath(
        string path,
        CancellationToken cancellationToken);

    Task<bool> TryLockPath(
        string path,
        Func<Task> task,
        CancellationToken cancellationToken);

    Task<bool> TryLockDatasetVersionExclusive(
        DatasetVersion datasetVersion,
        Func<Task> task,
        CancellationToken cancellationToken);

    Task<bool> TryLockDatasetVersionShared(
        DatasetVersion datasetVersion,
        Func<Task> task,
        CancellationToken cancellationToken);
}
