using DorisStorageAdapter.Services.Contract.Models;
using System.Collections.Generic;

namespace DorisStorageAdapter.Services.Implementation.Services.Audit;

internal record AuditOperation
{
    public required string Action { get; init; }

    public DatasetVersion? DatasetVersion { get; init; }
    public string? ResourceId { get; init; }

    public IReadOnlyDictionary<string, object?> Metadata { get; init; }
        = new Dictionary<string, object?>();
}
