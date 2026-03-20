using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DorisStorageAdapter.Services.Implementation.Storage;

internal interface IStorageProviderRegistrar
{
    static abstract string ProviderKey { get; }

    static abstract void AddProvider(
        IServiceCollection services, IConfiguration providerConfiguration);
}