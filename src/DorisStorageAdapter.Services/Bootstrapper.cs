using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Implementation;
using DorisStorageAdapter.Services.Implementation.Configuration;
using DorisStorageAdapter.Services.Implementation.Lock;
using DorisStorageAdapter.Services.Implementation.Storage;
using Medallion.Threading;
using Medallion.Threading.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using System;
using System.Linq;

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

        services.AddSingleton<ILockService, RedisLockService>();
        services.AddTransient<IDatasetVersionService, DatasetVersionService>();
        services.AddTransient<IFileService, FileService>();
        services.AddTransient<MetadataService>();
        services.AddSingleton<IStoragePathLock, StoragePathLock>();

        var connection = ConnectionMultiplexer.Connect("localhost"); // uses StackExchange.Redis
        var redisLockProvider = new RedisDistributedSynchronizationProvider(connection.GetDatabase());
        services.AddSingleton<IDistributedLockProvider>(redisLockProvider);
        services.AddSingleton<IDistributedReaderWriterLockProvider>(redisLockProvider);

        SetupStorageService(services, configuration);
    }

    // Setup storage service based on configuration
    private static void SetupStorageService(IServiceCollection services, IConfiguration configuration)
    {
        var configSection = configuration.GetSection(StorageConfiguration.ConfigurationSection);
        var storageConfiguration = configSection.Get<StorageConfiguration>()!;
        string storageService = storageConfiguration.ActiveStorageService;

        var types = typeof(Bootstrapper).Assembly.GetTypes()
            .Where(t =>
                t.GetInterfaces().Any(i =>
                    i.IsGenericType &&
                    i.GetGenericTypeDefinition() == typeof(IStorageServiceConfigurer<>) &&
                    i.GenericTypeArguments[0].Name == storageService))
            .ToList();

        if (types.Count == 0)
        {
            throw new StorageConfigurationException($"No implementation of '{storageService}' found.");
        }
        else if (types.Count > 1)
        {
            throw new StorageConfigurationException($"Multiple implementations of '{storageService}' found.");
        }

        var configurer = Activator.CreateInstance(types[0]) as IStorageServiceConfigurerBase;
        configurer!.Configure(services, configSection.GetSection(storageService));
    }
}
