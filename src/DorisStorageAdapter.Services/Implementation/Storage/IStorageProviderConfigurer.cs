using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DorisStorageAdapter.Services.Implementation.Storage;

internal interface IStorageProviderConfigurerBase
{
    void Configure(IServiceCollection services, IConfiguration configuration);
}

internal interface IStorageProviderConfigurer<T> : IStorageProviderConfigurerBase where T : IStorageProvider
{
    static abstract string ProviderKey { get; }
}
