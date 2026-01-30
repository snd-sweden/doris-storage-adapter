namespace DorisStorageAdapter.Services.Contract.Models;

public sealed record StorageLimits(
    int MaxFileCount,
    long MaxFileSize,
    long MaxTotalSize);
