using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DorisStorageAdapter.Server.Configuration;

public sealed record AuthorizationConfiguration
{
    public const string ConfigurationSection = "Authorization";

    [Required]
    public required bool AllowReadUnpublishedData { get; init; }

    [Required]
    public required IEnumerable<string> CorsAllowedOrigins { get; init; }

    [Required]
    public required Uri JwksUri { get; init; }
}
