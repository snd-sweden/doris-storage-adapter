using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DorisStorageAdapter.Services.Implementation.Storage.InMemory;

internal sealed class InMemoryStorageProviderConfigurer : IStorageProviderConfigurer<InMemoryStorageProvider>
{
    public static string ProviderKey => "InMemory";

    public void Configure(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<InMemoryStorage>();
        services.AddTransient<IStorageProvider, InMemoryStorageProvider>();
    }
}
