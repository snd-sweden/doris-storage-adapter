using System.Net;

namespace DorisStorageAdapter.Services.Contract.Audit;

public sealed record AuditContext
{
    public required AuditInitiatorType InitiatorType { get; init; }
    public AuditUser? User { get; init; }

    public string? TraceId { get; init; }
    public IPAddress? IPAddress { get; init; }
}
