using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.Configuration;
using Microsoft.Extensions.Options;

namespace DorisStorageAdapter.Services.Implementation;

internal sealed class SystemService(
    IOptions<StorageConfiguration> storageConfiguration) : ISystemService
{
    private readonly SystemInformation systemInformation = new(
        storageConfiguration.Value.ActiveStorageService);

    public SystemInformation GetSystemInformation() => systemInformation;
}
