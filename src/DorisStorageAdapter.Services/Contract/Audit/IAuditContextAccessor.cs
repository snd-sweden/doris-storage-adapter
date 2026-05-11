namespace DorisStorageAdapter.Services.Contract.Audit;

public interface IAuditContextAccessor
{
    AuditContext Current { get; }
}
