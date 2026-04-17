using DorisStorageAdapter.Services.Contract.Models;

namespace DorisStorageAdapter.Server.Controllers.Models.Requests;

public sealed record PublishRequest(
    AccessRight AccessRight,
    string CanonicalDoi,
    string Doi);