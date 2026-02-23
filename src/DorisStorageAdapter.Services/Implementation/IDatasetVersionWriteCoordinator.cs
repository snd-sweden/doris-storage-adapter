using DorisStorageAdapter.Services.Contract.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation;

internal interface IDatasetVersionWriteCoordinator
{
    Task<T> WithWriteAccess<T>(
        DatasetVersion datasetVersion,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);

    Task WithWriteAccess(
        DatasetVersion datasetVersion,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken);
}
