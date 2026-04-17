namespace DorisStorageAdapter.Services.Contract.Models;

public sealed record SystemInformation(
    DatasetAccessMode DatasetAccessMode,
    string StorageProvider);
    
