namespace DorisStorageAdapter.Server.Controllers.Dtos;

public record SystemInformationDto(
    int MaxFileCount,
    long MaxFileSize,
    long MaxTotalSize,
    string StorageType,
    string Version);