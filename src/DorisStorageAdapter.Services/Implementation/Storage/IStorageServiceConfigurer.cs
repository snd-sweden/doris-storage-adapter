﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DorisStorageAdapter.Services.Implementation.Storage;

internal interface IStorageServiceConfigurerBase
{
    void Configure(IServiceCollection services, IConfiguration configuration);
}

internal interface IStorageServiceConfigurer<T> : IStorageServiceConfigurerBase where T : IStorageService
{
}
