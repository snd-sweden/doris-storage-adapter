using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.Storage;

namespace DorisStorageAdapter.Services.Implementation.Services.Bags;

internal sealed class BagContextFactory(IStorageProvider storageProvider)
{
    public BagContext Create(string storagePath) => new(storagePath, storageProvider);

    public BagContext Create(DatasetVersion datasetVersion) => 
        Create(DatasetVersionStoragePaths.GetDatasetVersionPath(datasetVersion));
}
