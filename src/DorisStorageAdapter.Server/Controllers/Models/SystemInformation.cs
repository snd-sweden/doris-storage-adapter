namespace DorisStorageAdapter.Server.Controllers.Models;

public record SystemInformation(
    bool AllowPublicAccessRight,
    bool AllowReadDraftFiles,
    string StorageType,
    string Version);