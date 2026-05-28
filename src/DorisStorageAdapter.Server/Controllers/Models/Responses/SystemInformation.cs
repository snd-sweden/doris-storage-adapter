using DorisStorageAdapter.Services.Contract.Models;

namespace DorisStorageAdapter.Server.Controllers.Models.Responses;

public sealed record SystemInformation(
    bool AllowReadDraftFiles,
    DatasetAccessMode DatasetAccessMode,
    string StorageProvider,
    bool TenancyEnabled,
    string Version);