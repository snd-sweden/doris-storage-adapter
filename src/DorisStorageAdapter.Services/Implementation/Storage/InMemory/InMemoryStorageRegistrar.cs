using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DorisStorageAdapter.Services.Implementation.Storage.InMemory;

internal sealed class InMemoryStorageRegistrar : IStorageProviderRegistrar
{
    public static string ProviderKey => "InMemory";

    public static void AddProvider(
        IServiceCollection services, IConfiguration providerConfiguration)
    {
        services.AddSingleton<InMemoryStorage>();
        services.AddTransient<IStorageProvider, InMemoryStorageProvider>();
    }
}
