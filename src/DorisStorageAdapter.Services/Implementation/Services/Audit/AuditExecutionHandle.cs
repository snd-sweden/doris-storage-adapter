using DorisStorageAdapter.Services.Contract.Audit;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services.Audit;

internal sealed class AuditExecutionHandle(
    AuditOperation operation,
    AuditContext context,
    AuditExecutionState state,
    IAuditSink auditSink,
    TimeProvider timeProvider,
    DateTime startedAt,
    bool enabled)
{
    private readonly AuditOperation _operation = operation;
    private readonly AuditContext _context = context;
    private readonly AuditExecutionState _state = state;
    private readonly IAuditSink _auditSink = auditSink;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly DateTime _startedAt = startedAt;
    private readonly bool _enabled = enabled;

    private int _completed;

    public AuditExecutionState State => _state;

    public async ValueTask CompleteAsync(AuditOutcome outcome)
    {
        if (!_enabled)
        {
            return;
        }

        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        var record = new AuditRecord
        {
            Operation = _operation,
            Execution = new AuditExecution
            {
                Outcome = outcome,
                Details = new Dictionary<string, object>(_state.Details)
            },
            Context = _context,
            StartedAt = _startedAt,
            EndedAt = _timeProvider.GetUtcNow().UtcDateTime
        };

        await _auditSink.WriteAsync(record);
    }
}
