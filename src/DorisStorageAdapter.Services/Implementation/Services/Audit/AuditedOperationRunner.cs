using DorisStorageAdapter.Services.Contract.Audit;
using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Implementation.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services.Audit;

internal sealed class AuditedOperationRunner(
    IOptions<AuditConfiguration> configuration,
    IAuditSink auditSink,
    IAuditContextAccessor contextAccessor,
    TimeProvider timeProvider)
{
    private readonly AuditConfiguration _configuration = configuration.Value;
    private readonly IAuditSink _auditSink = auditSink;
    private readonly IAuditContextAccessor _contextAccessor = contextAccessor;
    private readonly TimeProvider _timeProvider = timeProvider;

    public Task<T> RunAsync<T>(
        AuditOperation operation,
        Func<AuditExecutionState, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        return StartAsync(
            operation,
            async (handle, ct) =>
            {
                T result = await action(handle.State, ct);

                await handle.CompleteAsync(AuditOutcome.Success);

                return result;
            },
            cancellationToken);
    }

    public Task RunAsync(
        AuditOperation operation,
        Func<AuditExecutionState, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        return RunAsync(
            operation,
            async (state, ct) =>
            {
                await action(state, ct);
                return new NoResult();
            },
            cancellationToken);
    }

    public async Task<T> StartAsync<T>(
        AuditOperation operation,
        Func<AuditExecutionHandle, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(action);

        var handle = new AuditExecutionHandle(
            operation,
            _contextAccessor.Current,
            new AuditExecutionState(),
            _auditSink,
            _timeProvider,
            _timeProvider.GetUtcNow().UtcDateTime,
            _configuration.Enabled);

        try
        {
            return await action(handle, cancellationToken);
        }
        catch (Exception ex)
        {
            if (ShouldRecord(ex))
            {
                await handle.CompleteAsync(
                    AuditOutcomeMapper.FromException(
                        ex, cancellationToken));
            }

            throw;
        }
    }

    public Task StartAsync(
        AuditOperation operation,
        Func<AuditExecutionHandle, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        return StartAsync(
            operation,
            async (handle, ct) =>
            {
                await action(handle, ct);
                return new NoResult();
            },
            cancellationToken);
    }

    private static bool ShouldRecord(Exception exception) =>
        exception is not ServiceException;

    private readonly struct NoResult;
}