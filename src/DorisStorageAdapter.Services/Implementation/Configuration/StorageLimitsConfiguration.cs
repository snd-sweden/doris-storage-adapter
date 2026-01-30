using System.ComponentModel.DataAnnotations;

namespace DorisStorageAdapter.Services.Implementation.Configuration;

internal record StorageLimitsConfiguration
{
    public const string ConfigurationSection = "Storage:Limits";

    // Definiera övre gräns för vad MaxFileCount får sättas till?
    // I så fall både här och vid uppdatering via API.

    [Required]
    public required int MaxFileCount { get; init; }
    [Required]
    public required long MaxFileSize { get; init; }
    [Required]
    public required long MaxTotalSize { get; init; }
}