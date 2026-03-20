using DorisStorageAdapter.Services.Implementation.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DorisStorageAdapter.Services.Implementation.Storage;

internal static class ServiceCollectionExtensions
{
    private delegate void ConfigureStorageProvider(
       IServiceCollection services,
       IConfiguration providerConfiguration);

    public static IServiceCollection AddStorageProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptionsWithValidateOnStart<StorageConfiguration>()
           .Bind(configuration.GetSection(StorageConfiguration.ConfigurationSection))
           .ValidateDataAnnotations();

        var storageSection = configuration.GetSection(StorageConfiguration.ConfigurationSection);
        var storageConfiguration = storageSection.Get<StorageConfiguration>();

        if (storageConfiguration == null ||
            string.IsNullOrWhiteSpace(storageConfiguration.Provider))
        {
            throw new OptionsValidationException(
                nameof(StorageConfiguration),
                typeof(StorageConfiguration),
                ["Storage provider is not configured."]);
        }

        var registrations = CreateRegistrations();
        if (!registrations.TryGetValue(storageConfiguration.Provider, out var configure))
        {
            throw new OptionsValidationException(
                nameof(StorageConfiguration),
                typeof(StorageConfiguration),
                [$"Unknown storage provider '{storageConfiguration.Provider}'."]);
        }

        var providerSection = storageSection.GetSection(storageConfiguration.Provider);
        configure(services, providerSection);

        return services;
    }

    private static ReadOnlyDictionary<string, ConfigureStorageProvider> CreateRegistrations()
    {
        var registrations = new Dictionary<string, ConfigureStorageProvider>(StringComparer.OrdinalIgnoreCase);

        void Add<TRegistrar>()
            where TRegistrar : IStorageProviderRegistrar
        {
            string providerKey = TRegistrar.ProviderKey;

            if (!registrations.TryAdd(
                providerKey,
                static (services, providerConfiguration) =>
                {
                    TRegistrar.AddProvider(services, providerConfiguration);
                }))
            {
                throw new InvalidOperationException(
                    $"A storage provider with key '{providerKey}' is already registered.");
            }
        }

        Add<FileSystem.FileSystemStorageRegistrar>();
        Add<InMemory.InMemoryStorageRegistrar>();
        Add<NextCloud.NextCloudStorageRegistrar>();
        Add<S3.S3StorageRegistrar>();

        return registrations.AsReadOnly();
    }
}