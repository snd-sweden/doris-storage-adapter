using DorisStorageAdapter.Server.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System;

namespace DorisStorageAdapter.Server.Tenancy;

internal class SubdomainTenantResolver(
    IOptions<GeneralConfiguration> generalConfiguration) : ITenantResolver
{
    private readonly GeneralConfiguration _generalConfiguration = generalConfiguration.Value;

    public string? ResolveTenantId(HttpContext context)
    {
        var baseHost = _generalConfiguration.PublicUrl.Host;
        var requestHost = context.Request.Host.Host;

        if (string.Equals(requestHost, baseHost, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!requestHost.EndsWith("." + baseHost, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var subdomain = requestHost[..^(baseHost.Length + 1)];

        return string.IsNullOrWhiteSpace(subdomain)
            ? null
            : subdomain;
    }
}
