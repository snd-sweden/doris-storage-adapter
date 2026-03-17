using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Implementation.Configuration;
using DorisStorageAdapter.Services.Implementation.Locking;
using DorisStorageAdapter.Services.Implementation.Locking.InProcess;
using DorisStorageAdapter.Services.Implementation.Services;
using DorisStorageAdapter.Services.Implementation.Services.Bags;
using DorisStorageAdapter.Services.Implementation.Services.Locking;
using DorisStorageAdapter.Services.Implementation.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reflection;

namespace DorisStorageAdapter.Services;

public static class Bootstrapper
{
    public static void SetupServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptionsWithValidateOnStart<StorageConfiguration>()
            .Bind(configuration.GetSection(StorageConfiguration.ConfigurationSection))
            .ValidateDataAnnotations();

        services.AddSingleton<ILockProvider, InProcessLockProvider>();
        services.AddSingleton<IReaderWriterLockProvider, InProcessReaderWriterLockProvider>();
        services.AddSingleton<IStorageLockProvider, StorageLockProvider>();
        services.AddSingleton<DatasetVersionLocks>();
        services.AddTransient<BagContextFactory>();
        services.AddTransient<IFileService, FileService>();
        services.AddTransient<IStatusService, StatusService>();
        services.AddSingleton<ISystemService, SystemService>();

        SetupStorageProvider(services, configuration);
    }

    // Setup storage provider based on configuration.
    private static void SetupStorageProvider(IServiceCollection services, IConfiguration configuration)
    {
        var configSection = configuration.GetSection(StorageConfiguration.ConfigurationSection);

        var storageConfiguration = configSection.Get<StorageConfiguration>()
            ?? throw new StorageConfigurationException(
                $"Missing or invalid '{StorageConfiguration.ConfigurationSection}' configuration.");

        var providerKey = storageConfiguration.Provider;

        if (string.IsNullOrWhiteSpace(providerKey))
        {
            throw new StorageConfigurationException("Storage provider is not configured.");
        }

        var matchingTypes = typeof(Bootstrapper).Assembly.GetTypes()
            .Where(t =>
                !t.IsAbstract &&
                !t.IsInterface &&
                t.GetInterfaces().Any(i =>
                    i.IsGenericType &&
                    i.GetGenericTypeDefinition() == typeof(IStorageProviderConfigurer<>)))
            .Where(t =>
                string.Equals(
                    t.GetProperty(
                        "ProviderKey", 
                        BindingFlags.Public | BindingFlags.Static
                    )?.GetValue(null) as string,
                    providerKey,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingTypes.Count == 0)
        {
            throw new StorageConfigurationException($"No implementation for '{providerKey}' found.");
        }

        if (matchingTypes.Count > 1)
        {
            throw new StorageConfigurationException($"Multiple implementations for '{providerKey}' found.");
        }

        var configurerType = matchingTypes[0];

        if (configurerType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new StorageConfigurationException(
                $"Configurer '{configurerType.FullName}' must have a public parameterless constructor.");
        }

        var configurer = Activator.CreateInstance(configurerType) as IStorageProviderConfigurerBase
            ?? throw new StorageConfigurationException(
                $"Could not create configurer '{configurerType.FullName}'.");

        configurer.Configure(services, configSection.GetSection(providerKey));
    }
}
