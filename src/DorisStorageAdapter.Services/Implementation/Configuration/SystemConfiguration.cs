using DorisStorageAdapter.Services.Contract.Models;
using System.ComponentModel.DataAnnotations;

namespace DorisStorageAdapter.Services.Implementation.Configuration;

internal sealed record SystemConfiguration
{
    public const string ConfigurationSection = "System";

    [Required]
    [EnumDataType(typeof(DatasetAccessMode))]
    public required DatasetAccessMode DatasetAccessMode { get; init; }

    public bool EnableTenancy { get; init; }
}
