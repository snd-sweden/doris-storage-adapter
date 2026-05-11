using DorisStorageAdapter.Services.Contract.Audit;
using DorisStorageAdapter.Services.Contract.Exceptions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services.Audit;

internal sealed class AuditedOperationRunner(
    IAuditSink auditLog,
    IAuditContextAccessor contextAccessor)
{
    private readonly IAuditSink _auditLog = auditLog;
    private readonly IAuditContextAccessor _contextAccessor = contextAccessor;

    public async Task<T> RunAsync<T>(
        AuditOperation operation,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            var result = await action(cancellationToken);

            await RecordAsync(
                operation, 
                AuditOutcome.Success, 
                errorCode: null, 
                cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            var outcome = Classify(ex, cancellationToken);

            await RecordAsync(
                operation,
                outcome,
                errorCode: GetErrorCode(ex),
                cancellationToken: CancellationToken.None);

            throw;
        }
    }

    private async Task RecordAsync(
        AuditOperation operation,
        AuditOutcome outcome,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        var context = _contextAccessor.Current;

        await _auditLog.WriteAsync(new AuditRecord
        {
            Context = context,
            Operation = operation,
            Outcome = outcome,
            ErrorCode = errorCode,
        }, 
        cancellationToken);
    }

    private static AuditOutcome Classify(
        Exception ex, CancellationToken cancellationToken)
    {
        if (ex is OperationCanceledException && 
            cancellationToken.IsCancellationRequested)
        {
            return AuditOutcome.Cancelled;
        }

        if (ex is ConflictException ||
            ex is DatasetStatusException ||
            ex is ValidationException)
        {
            return AuditOutcome.Rejected;
        }

        return AuditOutcome.Failed;
    }

    private static string? GetErrorCode(Exception ex)
    {
        return ex.GetType().Name;
    }
}