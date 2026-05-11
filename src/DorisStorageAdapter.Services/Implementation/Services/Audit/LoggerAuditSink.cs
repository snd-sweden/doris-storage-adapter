using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services.Audit;

internal sealed partial class LoggerAuditSink(ILoggerFactory loggerFactory) : IAuditSink
{
    private readonly ILogger _logger = loggerFactory.CreateLogger("DorisStorageAdapter.Audit");

    private sealed record AuditLogEntry
    {
        public required DateTime Timestamp { get; init; }

        //public required string Action { get; init; }
        //public required string Outcome { get; init; }

        public string? ErrorCode { get; init; }

        public string? InitatorType { get; init; }
        public string? ResourceId { get; init; }
        public string? DatasetIdentifier { get; init; }
        public string? DatasetVersion { get; init; }

        public string? UserEduPersonPrincipalName { get; init; }
        public string? UserEmail { get; init; }
        public string? UserFamilyName { get; init; }
        public string? UserGivenName { get; init; }
        public string? UserName { get; init; }
        public string? UserOrcid { get; init; }

        public string? IPAddress { get; init; }
        public string? TraceId { get; init; }

        public required IReadOnlyDictionary<string, object?> Metadata { get; init; }

        public static AuditLogEntry From(AuditRecord record) => new()
        {
            Timestamp = record.Timestamp,
            //Action = record.Operation.Action,
            //Outcome = record.Outcome.ToString(),
            ErrorCode = record.ErrorCode,

            UserEduPersonPrincipalName = record.Context.User?.EduPersonPrincipalName,
            UserEmail = record.Context.User?.Email,
            UserFamilyName = record.Context.User?.FamilyName,
            UserGivenName = record.Context.User?.GivenName,
            UserName = record.Context.User?.Name,
            UserOrcid = record.Context.User?.Orcid,

            IPAddress = record.Context.IPAddress?.ToString(),
            TraceId = record.Context.TraceId,

            Metadata = record.Operation.Metadata
        };
    }

    [LoggerMessage(
        EventId = 1000,
        EventName = "Audit",
        Level = LogLevel.Information,
        Message = "{Action} completed with outcome {Outcome}")]
    private partial void LogAudit(
        string action,
        string outcome,
        [LogProperties(
            OmitReferenceName = true, 
            SkipNullProperties = true)]
        AuditLogEntry entry);

    public async ValueTask WriteAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        if (!_logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        string action = record.Operation.Action;
        string outcome = record.Outcome.ToString();
        var entry = AuditLogEntry.From(record);

        LogAudit(
            action,
            outcome,
            entry);
    }
}
