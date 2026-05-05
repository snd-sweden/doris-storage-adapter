namespace DorisStorageAdapter.Services.Contract.Models;

public sealed record DatasetVersion(
    string Identifier,
    string Version,
    string? TenantId);
