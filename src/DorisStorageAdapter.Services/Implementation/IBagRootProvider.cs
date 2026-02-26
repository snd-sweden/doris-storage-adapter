namespace DorisStorageAdapter.Services.Implementation;

internal interface IBagRootProvider
{
    BagRoot Create(string basePath);
}
