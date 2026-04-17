using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DorisStorageAdapter.Server.Configuration;

public sealed record SecurityConfiguration
{
    public const string ConfigurationSection = "Security";

    [Required]
    public required bool AllowReadDraftFiles { get; init; }

    [Required]
    public required IEnumerable<string> CorsAllowedOrigins { get; init; }

    [Required]
    public required Uri JwksUri { get; init; }
}
