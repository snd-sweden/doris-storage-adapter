using System.ComponentModel.DataAnnotations;

namespace DorisStorageAdapter.Services.Implementation.Configuration;

internal sealed record PublicationConfiguration
{
    public const string ConfigurationSection = "Publication";

    [Required]
    public required bool AllowPublicAccessRight { get; init; }
}
