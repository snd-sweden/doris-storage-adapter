using System.Collections.Generic;

namespace DorisStorageAdapter.Services.Implementation.Services.Audit;

internal sealed record AuditExecutionState
{
    public IDictionary<string, object> Details { get; } =
        new Dictionary<string, object>();
}
