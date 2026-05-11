using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services.Audit;

internal interface IAuditSink
{
    ValueTask WriteAsync(AuditRecord auditRecord, CancellationToken cancellationToken);
}
