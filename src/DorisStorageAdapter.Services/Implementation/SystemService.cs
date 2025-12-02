using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.Configuration;
using Microsoft.Extensions.Options;

namespace DorisStorageAdapter.Services.Implementation;

internal sealed class SystemService(
    IOptions<StorageConfiguration> storageConfiguration,
    IOptions<LimitsConfiguration> limitsConfiguration) : ISystemService
{
    private readonly SystemInformation systemInformation = new(
        MaxFileCount: limitsConfiguration.Value.MaxFileCount,
        MaxFileSize: limitsConfiguration.Value.MaxFileSize,
        MaxTotalSize: limitsConfiguration.Value.MaxTotalSize,
        storageConfiguration.Value.ActiveStorageService);

    public SystemInformation GetSystemInformation() => systemInformation;
}
