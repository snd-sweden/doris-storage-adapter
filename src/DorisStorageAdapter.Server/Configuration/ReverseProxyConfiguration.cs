using System.Collections;
using System.Collections.Generic;

namespace DorisStorageAdapter.Server.Configuration;

internal sealed record ReverseProxyConfiguration
{
    public const string ConfigurationSection = "ReverseProxy";

    public int ForwardLimit { get; init; } = 1;
    public IEnumerable<string> KnownAddresses { get; init; } = [];
    public IEnumerable<string> KnownNetworks { get; init; } = [];
}
