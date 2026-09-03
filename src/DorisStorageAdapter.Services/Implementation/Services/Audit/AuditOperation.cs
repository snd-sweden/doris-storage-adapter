using DorisStorageAdapter.Services.Contract.Models;
using System.Collections.Generic;

namespace DorisStorageAdapter.Services.Implementation.Services.Audit;

internal sealed record AuditOperation
{
    public required string Action { get; init; }

    public DatasetVersion? DatasetVersion { get; init; }
    public string? Target { get; init; }

    public IReadOnlyDictionary<string, object> Details { get; init; }
        = new Dictionary<string, object>();
}
