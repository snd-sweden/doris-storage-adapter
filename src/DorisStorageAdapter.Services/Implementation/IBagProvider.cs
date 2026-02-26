using DorisStorageAdapter.Services.Contract.Models;

namespace DorisStorageAdapter.Services.Implementation;

internal interface IBagProvider
{
    Bag Create(string path);

    Bag Create(DatasetVersion datasetVersion) => Create(Paths.GetDatasetVersionPath(datasetVersion));
}
