using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.Configuration;
using DorisStorageAdapter.Services.Implementation.Storage;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services;

internal sealed class SystemService(
    IOptions<SystemConfiguration> systemConfiguration,
    IOptions<StorageConfiguration> storageConfiguration,
    IStorageProvider storageProvider,
    HybridCache cache) : ISystemService
{
    private const string ReadinessCacheKey = "system:readiness";

    private readonly SystemInformation systemInformation = new(
        systemConfiguration.Value.DatasetAccessMode,
        storageConfiguration.Value.Provider,
        systemConfiguration.Value.EnableTenancy);

    private readonly IStorageProvider _storageProvider = storageProvider;

    public SystemInformation GetSystemInformation() => systemInformation;

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        // Cache value for 10 seconds to avoid flooding the storage
        // provider with requests.

        return await cache.GetOrCreateAsync(
            ReadinessCacheKey,
            async ct => await _storageProvider.IsAvailableAsync(ct),
            new()
            {
                Expiration = TimeSpan.FromSeconds(10)
            },
            cancellationToken: cancellationToken);
    }
}
