using DorisStorageAdapter.Services.Implementation.Storage;

namespace DorisStorageAdapter.Services.Implementation;

internal sealed class BagProvider(IStorageService storageService) : IBagProvider
{
    public Bag Create(string basePath) => new(basePath, storageService);
}
