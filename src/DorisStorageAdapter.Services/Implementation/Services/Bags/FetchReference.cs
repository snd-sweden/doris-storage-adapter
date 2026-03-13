using DorisStorageAdapter.Services.Implementation.BagIt.Fetch;
using System.Collections.Generic;

namespace DorisStorageAdapter.Services.Implementation.Services.Bags;

internal sealed record FetchReference(
    string ReferencedBagStoragePath,
    string PathInBag,
    BagItFetchItem Item);