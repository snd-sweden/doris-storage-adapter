using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DorisStorageAdapter.Services.Implementation.Storage;

internal interface IStorageProviderConfigurer<TProvider> 
    where TProvider : IStorageProvider
{
    static abstract string ProviderKey { get; }

    void Configure(IServiceCollection services, IConfiguration configuration);
}
