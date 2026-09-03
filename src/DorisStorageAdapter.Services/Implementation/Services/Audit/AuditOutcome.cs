using System;

internal abstract record AuditOutcome
{
    private AuditOutcome()
    {
    }

    public static AuditOutcome Success { get; } =
        new SuccessOutcome();

    public static AuditOutcome Cancelled { get; } =
        new CancelledOutcome();

    public static AuditOutcome Failed(string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        return new FailureOutcome(errorCode);
    }

    public abstract string Name { get; }

    public virtual string? ErrorCode => null;

    public override string ToString() => Name;

    private sealed record SuccessOutcome : AuditOutcome
    {
        public override string Name => "Success";
    }

    private sealed record CancelledOutcome : AuditOutcome
    {
        public override string Name => "Cancelled";
    }

    private sealed record FailureOutcome(string Error) : AuditOutcome
    {
        public override string Name => "Failed";

        public override string ErrorCode => Error;
    }
}