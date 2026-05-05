using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.Configuration;
using Microsoft.Extensions.Options;

namespace DorisStorageAdapter.Services.Implementation.Services;

internal sealed class SystemService(
    IOptions<SystemConfiguration> systemConfiguration,
    IOptions<StorageConfiguration> storageConfiguration) : ISystemService
{
    private readonly SystemInformation systemInformation = new(
        systemConfiguration.Value.DatasetAccessMode,
        storageConfiguration.Value.Provider,
        systemConfiguration.Value.EnableTenancy);

    public SystemInformation GetSystemInformation() => systemInformation;
}
