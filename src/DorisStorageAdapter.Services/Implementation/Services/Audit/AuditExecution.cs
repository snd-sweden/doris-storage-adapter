using System.Collections.Generic;

namespace DorisStorageAdapter.Services.Implementation.Services.Audit;

internal sealed record AuditExecution
{
    public required AuditOutcome Outcome { get; init; }

    public IReadOnlyDictionary<string, object> Details { get; init; }
        = new Dictionary<string, object>();
}
