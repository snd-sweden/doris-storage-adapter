using DorisStorageAdapter.Services.Contract.Models;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Contract;

public interface IStatusService
{
    Task Publish(
        DatasetVersion datasetVersion, 
        AccessRight accessRight, 
        string canonicalDoi, 
        string doi, 
        CancellationToken cancellationToken);

    Task SetStatus(
        DatasetVersion datasetVersion,
        DatasetVersionStatus status,
        CancellationToken cancellationToken);
}
