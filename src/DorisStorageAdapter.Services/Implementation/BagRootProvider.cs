using DorisStorageAdapter.Services.Implementation.Storage;

namespace DorisStorageAdapter.Services.Implementation;

internal sealed class BagRootProvider(IStorageService storageService) : IBagRootProvider
{
    public BagRoot Create(string basePath) => new(basePath, storageService);
}
