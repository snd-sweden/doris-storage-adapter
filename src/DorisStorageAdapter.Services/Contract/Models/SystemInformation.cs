namespace DorisStorageAdapter.Services.Contract.Models;

public sealed record SystemInformation(
    int MaxFileCount,
    long MaxFileSize,
    long MaxTotalSize,
    string StorageType);
    
