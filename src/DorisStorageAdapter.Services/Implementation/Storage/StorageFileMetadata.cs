using System;

namespace DorisStorageAdapter.Services.Implementation.Storage;

internal sealed record StorageFileMetadata(
    DateTime? DateCreated,
    DateTime? DateModified,
    string Path,
    long Size
);
