using DorisStorageAdapter.Services.Contract.Audit;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services.Audit;

internal sealed class LoggerAuditSink(ILoggerFactory loggerFactory) : IAuditSink
{
    private static readonly EventId AuditEventId = new(1000, "Audit");

    private readonly ILogger _logger =
        loggerFactory.CreateLogger("DorisStorageAdapter.Audit");

    public ValueTask WriteAsync(AuditRecord record)
    {
        if (!_logger.IsEnabled(LogLevel.Information))
        {
            return ValueTask.CompletedTask;
        }

        var state = CreateState(record);

        _logger.Log(
            LogLevel.Information,
            AuditEventId,
            state,
            exception: null,
            static (state, _) => state.Message);

        return ValueTask.CompletedTask;
    }

    private static AuditLogState CreateState(AuditRecord record)
    {
        var state = new AuditLogState
        {
            Message = FormatMessage(record)
        };

        void Add(string name, object? value)
        {
            if (value != null)
            {
                state.Add(new(name, value));
            }
        }

        Add("Action",
            record.Operation.Action);

        Add("Outcome",
            record.Execution.Outcome.Name);

        Add("ErrorCode", record.Execution.Outcome.ErrorCode);

        Add("StartedAt",
            record.StartedAt.ToString("O"));

        Add("EndedAt",
           record.EndedAt.ToString("O"));

        Add("TenantId",
            record.Operation.DatasetVersion?.TenantId);

        Add("DatasetIdentifier",
            record.Operation.DatasetVersion?.Identifier);

        Add("DatasetVersion",
            record.Operation.DatasetVersion?.Version);

        Add("Target",
            record.Operation.Target);

        Add("InitiatorType",
            record.Context.InitiatorType.ToString());

        Add("UserEduPersonPrincipalName",
            record.Context.User?.EduPersonPrincipalName);

        Add("UserEmail",
            record.Context.User?.Email);

        Add("UserFamilyName",
            record.Context.User?.FamilyName);

        Add("UserGivenName",
            record.Context.User?.GivenName);

        Add("UserName",
            record.Context.User?.Name);

        Add("UserOrcid",
            record.Context.User?.Orcid);

        Add("IPAddress",
            record.Context.IPAddress?.ToString());

        Add("TraceId",
            record.Context.TraceId);

        foreach (var (key, value) in record.Operation.Details)
        {
            Add($"OperationDetail.{key}", value);
        }

        foreach (var (key, value) in record.Execution.Details)
        {
            Add($"ExecutionDetail.{key}", value);
        }

        return state;
    }

    private static string FormatMessage(AuditRecord record)
    {
        string actor = FormatActor(record.Context);
        string resource = FormatResource(record.Operation);
        string error = record.Execution.Outcome.ErrorCode == null
            ? string.Empty
            : $" ({record.Execution.Outcome.ErrorCode})";

        return
            $"{record.Operation.Action}{resource} by {actor}: " +
            $"{record.Execution.Outcome.Name}{error}";
    }


    private static string FormatResource(AuditOperation operation)
    {
        var parts = new List<string>();

        if (operation.DatasetVersion is { } datasetVersion)
        {
            if (datasetVersion.TenantId != null)
            {
                parts.Add(datasetVersion.TenantId);
            }

            parts.Add(datasetVersion.Identifier);
            parts.Add(datasetVersion.Version);
        }

        if (operation.Target != null)
        {
            parts.Add(operation.Target);
        }

        return parts.Count == 0
            ? string.Empty
            : $" {string.Join("/", parts)}";
    }

    private static string FormatActor(AuditContext context)
    {
        var user = context.User;

        string? userDisplay =
            user?.EduPersonPrincipalName
            ?? user?.Email
            ?? user?.Name;

        if (context.InitiatorType == AuditInitiatorType.Service)
        {
            return userDisplay == null
                ? "Service"
                : $"Service for {userDisplay}";
        }

        return userDisplay ?? "User";
    }

    private sealed class AuditLogState : List<KeyValuePair<string, object>>
    {
        public required string Message { get; init; }

        public override string ToString() => Message;
    }
}