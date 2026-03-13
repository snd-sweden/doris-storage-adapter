using System.Collections.Generic;

namespace DorisStorageAdapter.Services.Implementation.Services.Bags;

internal sealed record FetchReferenceGroup(
    string ReferencedBagStoragePath,
    IReadOnlyList<FetchReference> References);
