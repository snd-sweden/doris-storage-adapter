namespace DorisStorageAdapter.Server.Controllers.Models;

public record SystemInformation(
    bool AllowPublicAccessRight,
    bool AllowReadDraftFiles,
    string StorageProvider,
    string Version);