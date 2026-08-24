using System;
using System.Threading;

namespace DorisStorageAdapter.Services.Implementation.Services.Audit;

internal static class AuditOutcomeMapper
{
    public static AuditOutcome FromException(
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException &&
            cancellationToken.IsCancellationRequested)
        {
            return AuditOutcome.Cancelled;
        }

        return AuditOutcome.Failed(
            exception.GetType().Name);
    }
}
