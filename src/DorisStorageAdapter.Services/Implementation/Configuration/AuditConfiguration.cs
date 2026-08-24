namespace DorisStorageAdapter.Services.Implementation.Configuration;

internal sealed record AuditConfiguration
{
    public const string ConfigurationSection = "Audit";

    public bool Enabled { get; set; }
}
