using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DorisStorageAdapter.Services.Implementation.Storage.S3;

internal sealed class S3StorageProviderConfigurer : IStorageProviderConfigurer<S3StorageProvider>
{
    public static string ProviderKey => "S3";

    public void Configure(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptionsWithValidateOnStart<S3StorageConfiguration>()
           .Bind(configuration)
           .ValidateDataAnnotations()
           .Validate(c =>
                c.MultiPartUploadThreshold < c.MultiPartUploadChunkSize * 10_000,
                nameof(S3StorageConfiguration.MultiPartUploadChunkSize) +
                " is too small to allow uploading larger objects than the value of " +
                nameof(S3StorageConfiguration.MultiPartUploadThreshold) +
                " (max number of parts per upload is 10 000)");

        services.AddSingleton<IAmazonS3>(
            sp =>
            {
                var s3Config = sp.GetRequiredService<IOptions<S3StorageConfiguration>>().Value;

                return new AmazonS3Client(s3Config.AccessKey, s3Config.SecretKey, new AmazonS3Config
                {
                    // Disable retries to avoid seeking in the input stream
                    // when uploading objects, see S3StorageProvider.StoreAsync().
                    MaxErrorRetry = 0,
                    ServiceURL = s3Config.ServiceUrl,
                    ForcePathStyle = s3Config.ForcePathStyle,

                    RequestChecksumCalculation =
                        s3Config.RequestChecksumCalculationEnabled
                            ? RequestChecksumCalculation.WHEN_SUPPORTED
                            : RequestChecksumCalculation.WHEN_REQUIRED,

                    ResponseChecksumValidation =
                        s3Config.ResponseChecksumCalculationEnabled
                            ? ResponseChecksumValidation.WHEN_SUPPORTED
                            : ResponseChecksumValidation.WHEN_REQUIRED
                });
            });

        services.AddTransient<IStorageProvider, S3StorageProvider>();
    }
}
