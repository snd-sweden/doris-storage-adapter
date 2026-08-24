using DorisStorageAdapter.Services.Contract.Audit;
using System;

namespace DorisStorageAdapter.Services.Implementation.Services.Audit;

internal sealed record AuditRecord
{
    public required DateTime StartedAt { get; init; }
    public required DateTime EndedAt { get; init; }

    public required AuditOperation Operation { get; init; }
    public required AuditContext Context { get; init; }
    public required AuditExecution Execution { get; init; }
}
