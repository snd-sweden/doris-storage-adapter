using DorisStorageAdapter.Services.Contract.Models;

namespace DorisStorageAdapter.Server.Controllers.Models;

public record SystemInformation(
    bool AllowReadDraftFiles,
    DatasetAccessMode DatasetAccessMode,
    string StorageProvider,
    string Version);