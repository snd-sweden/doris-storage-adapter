using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Contract;

public interface ICheckService
{
    Task<IReadOnlyList<ErrorItem>> CheckConsistencyAsync(
        DatasetVersion datasetVersion,
        CancellationToken cancellationToken);
}
