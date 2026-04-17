using DorisStorageAdapter.Services.Contract.Models;

namespace DorisStorageAdapter.Server.Controllers.Models.Requests;

public sealed record SetStatusRequest(
    DatasetVersionStatus Status);
