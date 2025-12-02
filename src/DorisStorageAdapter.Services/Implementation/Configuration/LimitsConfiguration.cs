namespace DorisStorageAdapter.Services.Implementation.Configuration;

internal sealed record LimitsConfiguration
{
    public const string ConfigurationSection = "Limits";

    public required int MaxFileCount { get; init; } = 2000;
    public required long MaxFileSize { get; init; } = 5L * 1024 * 1024 * 1024;
    public required long MaxTotalSize { get; init; } = 50L * 1024 * 1024 * 1024; 
}
