using System.ComponentModel.DataAnnotations;

namespace DorisStorageAdapter.Services.Implementation.Configuration;

internal sealed record StorageConfiguration
{
    public const string ConfigurationSection = "Storage";

    [Required]
    public required string ActiveStorageService { get; init; }

    [Required]
    public required bool AllowPublicData { get; init; }
}
