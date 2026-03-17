using DorisStorageAdapter.Services.Implementation.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace DorisStorageAdapter.Services.Implementation.Storage;

internal sealed class StorageProviderRegistry
{
    private readonly Dictionary<string, Action<IServiceCollection, IConfiguration>> _registrations =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register<TProvider, TConfigurer>()
        where TProvider : IStorageProvider
        where TConfigurer : IStorageProviderConfigurer<TProvider>, new()
    {
        if (!_registrations.TryAdd(
                TConfigurer.ProviderKey,
                static (services, configuration) =>
                {
                    var configurer = new TConfigurer();
                    configurer.Configure(services, configuration);
                }))
        {
            throw new StorageConfigurationException(
                $"A storage provider configurer with key '{TConfigurer.ProviderKey}' is already registered.");
        }
    }

    public void ConfigureSelected(
        string providerKey,
        IServiceCollection services,
        IConfiguration providerConfiguration)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            throw new StorageConfigurationException("Storage provider is not configured.");
        }

        if (!_registrations.TryGetValue(providerKey, out var configure))
        {
            throw new StorageConfigurationException(
                $"No storage provider configurer for key '{providerKey}' is registered.");
        }

        configure(services, providerConfiguration);
    }
}
