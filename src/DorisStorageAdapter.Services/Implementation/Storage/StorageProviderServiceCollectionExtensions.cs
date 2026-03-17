using DorisStorageAdapter.Services.Implementation.Configuration;
using DorisStorageAdapter.Services.Implementation.Storage.FileSystem;
using DorisStorageAdapter.Services.Implementation.Storage.InMemory;
using DorisStorageAdapter.Services.Implementation.Storage.NextCloud;
using DorisStorageAdapter.Services.Implementation.Storage.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;

namespace DorisStorageAdapter.Services.Implementation.Storage;

internal static class StorageServiceProviderCollectionExtensions
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

    private static Dictionary<string, ConfigureStorageProvider> CreateRegistrations()
    {
        var registrations = new Dictionary<string, ConfigureStorageProvider>(StringComparer.OrdinalIgnoreCase);

        Register<FileSystemStorageProvider, FileSystemStorageProviderConfigurer>(registrations);
        Register<InMemoryStorageProvider, InMemoryStorageProviderConfigurer>(registrations);
        Register<NextCloudStorageProvider, NextCloudStorageProviderConfigurer>(registrations);
        Register<S3StorageProvider, S3StorageProviderConfigurer>(registrations);

        return registrations;
    }

    private static void Register<TProvider, TConfigurer>(
        IDictionary<string, ConfigureStorageProvider> registrations)
        where TProvider : IStorageProvider
        where TConfigurer : IStorageProviderConfigurer<TProvider>, new()
    {
        if (!registrations.TryAdd(
                TConfigurer.ProviderKey,
                static (services, providerConfiguration) =>
                {
                    var configurer = new TConfigurer();
                    configurer.Configure(services, providerConfiguration);
                }))
        {
            throw new InvalidOperationException(
                $"A storage provider with key '{TConfigurer.ProviderKey}' is already registered.");
        }
    }
}