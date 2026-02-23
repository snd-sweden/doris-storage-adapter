using DorisStorageAdapter.Services.Contract.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation;

internal interface IDatasetVersionStateRepository
{
    Task<bool> IsPublished(DatasetVersion v, CancellationToken ct);

    Task<DatasetVersionStatus?> GetStatus(DatasetVersion v, CancellationToken ct);
    Task<AccessRight?> GetAccessRight(DatasetVersion v, CancellationToken ct);

    Task<string> ValidateForPublish(DatasetVersion v, CancellationToken ct);

    Task Publish(DatasetVersion v, CancellationToken ct);

    Task SetDraftMetadata(DatasetVersion v, CancellationToken ct);
}
