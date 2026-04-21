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

namespace DorisStorageAdapter.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
       this IServiceCollection services,
       IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptionsWithValidateOnStart<SystemConfiguration>()
            .Bind(configuration.GetSection(SystemConfiguration.ConfigurationSection))
            .ValidateDataAnnotations();

        services.AddSingleton<ILockProvider, InProcessLockProvider>();
        services.AddSingleton<ISharedExclusiveLockProvider, InProcessSharedExclusiveLockProvider>();

        services.AddTransient<BagContextFactory>();
        services.AddSingleton<DatasetVersionLocks>();
        services.AddSingleton<IStorageLockProvider, StorageLockProvider>();
        services.AddTransient<ICheckService, CheckService>();
        services.AddTransient<IFileService, FileService>();
        services.AddTransient<IStatusService, StatusService>();
        services.AddSingleton<ISystemService, SystemService>();

        services.AddStorageProvider(configuration);

        return services;
    }
}
