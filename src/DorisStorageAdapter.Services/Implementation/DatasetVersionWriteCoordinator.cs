using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.Lock;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation;

internal class DatasetVersionWriteCoordinator(ILockService lockService) : IDatasetVersionWriteCoordinator
{
    public Task<T> WithWriteAccess<T>(DatasetVersion datasetVersion, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        lockService.TryLockDatasetVersionShared(datasetVersion, ct => action(ct), cancellationToken);
    }

    public Task WithWriteAccess(DatasetVersion datasetVersion, Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
