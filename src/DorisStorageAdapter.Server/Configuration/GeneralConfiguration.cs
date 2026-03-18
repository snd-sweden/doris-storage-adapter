using DorisStorageAdapter.Common;
using System;
using System.ComponentModel.DataAnnotations;

namespace DorisStorageAdapter.Server.Configuration;

internal sealed record GeneralConfiguration
{
    [Required]
    public required Uri PublicUrl
    {
        get;
        init => field = UriHelpers.EnsureUriEndsWithSlash(value);
    }
}
