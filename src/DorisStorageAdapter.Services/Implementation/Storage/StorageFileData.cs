using System.IO;

namespace DorisStorageAdapter.Services.Implementation.Storage;

internal sealed record StorageFileData(
    long Size,
    Stream Stream,
    long StreamLength);
