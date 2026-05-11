using DorisStorageAdapter.Services.Contract.Audit;
using System;

namespace DorisStorageAdapter.Services.Implementation.Services.Audit;

internal sealed record AuditRecord
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public required AuditContext Context { get; init; }
    public required AuditOutcome Outcome { get; init; }
    public required AuditOperation Operation { get; init; }

    public string? ErrorCode { get; init; }
}
