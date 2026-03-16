using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.Configuration;
using Microsoft.Extensions.Options;

namespace DorisStorageAdapter.Services.Implementation.Services;

internal sealed class SystemService(
    IOptions<PublicationConfiguration> publicationConfiguration,
    IOptions<StorageConfiguration> storageConfiguration) : ISystemService
{
    private readonly SystemInformation systemInformation = new(
        publicationConfiguration.Value.AllowPublicAccessRight,
        storageConfiguration.Value.ActiveStorageService);

    public SystemInformation GetSystemInformation() => systemInformation;
}
