using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Net.Http.Headers;
using WebDav;

namespace DorisStorageAdapter.Services.Implementation.Storage.NextCloud;

internal sealed class NextCloudStorageProviderConfigurer : IStorageProviderConfigurer<NextCloudStorageProvider>
{
    public static string ProviderKey => "NextCloud";

    public void Configure(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptionsWithValidateOnStart<NextCloudStorageConfiguration>()
           .Bind(configuration)
           .ValidateDataAnnotations()
           .Validate(c =>
                c.ChunkedUploadThreshold < c.ChunkedUploadChunkSize * 10_000,
                nameof(NextCloudStorageConfiguration.ChunkedUploadChunkSize) +
                " is too small to allow uploading larger files than the value of " +
                nameof(NextCloudStorageConfiguration.ChunkedUploadThreshold) +
                " (max number of chunks per upload is 10 000)")
           .Validate(c =>
                c.ChunkedUploadChunkSize >= 5_242_880 &&
                c.ChunkedUploadChunkSize <= 5_368_709_120,
                nameof(NextCloudStorageConfiguration.ChunkedUploadChunkSize) +
                " must be between 5MB and 5GB");

        services.AddTransient<IStorageProvider, NextCloudStorageProvider>();

        services.AddHttpClient<IWebDavClient, WebDavClient>((httpClient, sp) =>
        {
            var nextCloudConfiguration = sp.GetRequiredService<IOptions<NextCloudStorageConfiguration>>().Value;

            string authString = nextCloudConfiguration.User + ':' + nextCloudConfiguration.Password;
            string basicAuth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(authString));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

            return new WebDavClient(httpClient);
        });
    }
}
