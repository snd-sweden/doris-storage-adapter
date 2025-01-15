namespace DorisStorageAdapter.Configuration;

public sealed record StorageLimitsDatasetVersionPayloadConfiguration
{
    public const string ConfigurationSection = "Storage:Limits:DatasetVersionPayload";

    public long MaxFileSize { get; init; } = -1;
    public long MaxFileCount { get; init; } = -1;
    public long MaxTotalSize { get; init; } = -1;
}
