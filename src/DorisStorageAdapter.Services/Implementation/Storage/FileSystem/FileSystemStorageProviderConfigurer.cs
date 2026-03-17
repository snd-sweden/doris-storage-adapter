using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DorisStorageAdapter.Services.Implementation.Storage.FileSystem;

internal sealed class FileSystemStorageProviderConfigurer : IStorageProviderConfigurer<FileSystemStorageProvider>
{
    public static string ProviderKey => "FileSystem";

    public void Configure(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptionsWithValidateOnStart<FileSystemStorageConfiguration>()
           .Bind(configuration)
           .ValidateDataAnnotations();

        services.AddTransient<IStorageProvider, FileSystemStorageProvider>();
    }
}
