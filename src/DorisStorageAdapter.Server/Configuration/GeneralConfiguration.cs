using DorisStorageAdapter.Common;
using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace DorisStorageAdapter.Server.Configuration;

internal sealed record GeneralConfiguration
{
    private readonly Uri _publicUrl;

    [Required]
    public required Uri PublicUrl
    {
        get => _publicUrl;

        [MemberNotNull(nameof(_publicUrl))]
        init
        {
            _publicUrl = UriHelpers.EnsureUriEndsWithSlash(value);
        }
    }
}
