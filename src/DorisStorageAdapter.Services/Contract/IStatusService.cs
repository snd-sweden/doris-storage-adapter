using DorisStorageAdapter.Services.Contract.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Contract;

public interface IStatusService
{
    Task PublishAsync(
        DatasetVersion datasetVersion, 
        AccessRight accessRight, 
        string canonicalDoi, 
        string doi,
        DateTime publishedDate,
        CancellationToken cancellationToken);

    Task SetStatusAsync(
        DatasetVersion datasetVersion,
        DatasetVersionStatus status,
        CancellationToken cancellationToken);
}
