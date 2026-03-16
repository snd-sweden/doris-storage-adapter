using DorisStorageAdapter.Services.Implementation.BagIt.Fetch;

namespace DorisStorageAdapter.Services.Implementation.Services.Bags;

internal sealed record FetchReference(
    string ReferencedBagStoragePath,
    string PathInBag,
    BagItFetchItem Item);