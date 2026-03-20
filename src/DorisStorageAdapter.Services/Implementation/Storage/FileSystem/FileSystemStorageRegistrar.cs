using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DorisStorageAdapter.Services.Implementation.Storage.FileSystem;

internal sealed class FileSystemStorageRegistrar : IStorageProviderRegistrar
{
    public static string ProviderKey => "FileSystem";

    public static void AddProvider(
        IServiceCollection services, IConfiguration providerConfiguration)
    {
        services.AddOptionsWithValidateOnStart<FileSystemStorageConfiguration>()
           .Bind(providerConfiguration)
           .ValidateDataAnnotations();

        services.AddTransient<IStorageProvider, FileSystemStorageProvider>();
    }
}
