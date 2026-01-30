using DorisStorageAdapter.Services.Contract.Models;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Contract;

public interface IStorageLimitsService
{
    Task<StorageLimits> GetStorageLimits(
        DatasetVersion datasetVersion,
        CancellationToken cancellationToken);

    Task SetStorageLimits(
        DatasetVersion datasetVersion,
        StorageLimits storageLimits,
        CancellationToken cancellationToken);
}
