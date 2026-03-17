using DorisStorageAdapter.Services.Implementation.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace DorisStorageAdapter.Services.Implementation.Storage;

internal static class StorageServiceCollectionExtensions
{
    private static readonly StorageProviderRegistry _registry = CreateRegistry();

    public static IServiceCollection AddStorageProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var storageSection = configuration.GetSection(StorageConfiguration.ConfigurationSection);

        var storageConfiguration = storageSection.Get<StorageConfiguration>()
            ?? throw new StorageConfigurationException(
                $"Missing or invalid '{StorageConfiguration.ConfigurationSection}' configuration.");

        if (string.IsNullOrWhiteSpace(storageConfiguration.Provider))
        {
            throw new StorageConfigurationException("Storage provider is not configured.");
        }

        var providerSection = storageSection.GetSection(storageConfiguration.Provider);

        _registry.ConfigureSelected(
            storageConfiguration.Provider,
            services,
            providerSection);

        return services;
    }

    private static StorageProviderRegistry CreateRegistry()
    {
        var registry = new StorageProviderRegistry();

        registry.Register<FileSystem.FileSystemStorageProvider, FileSystem.FileSystemStorageProviderConfigurer>();
        registry.Register<InMemory.InMemoryStorageProvider, InMemory.InMemoryStorageProviderConfigurer>();
        registry.Register<NextCloud.NextCloudStorageProvider, NextCloud.NextCloudStorageProviderConfigurer>();
        registry.Register<S3.S3StorageProvider, S3.S3StorageProviderConfigurer>();

        return registry;
    }
}