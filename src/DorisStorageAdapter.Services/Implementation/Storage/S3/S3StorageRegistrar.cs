using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;

namespace DorisStorageAdapter.Services.Implementation.Storage.S3;

internal sealed class S3StorageRegistrar : IStorageProviderRegistrar
{
    public static string ProviderKey => "S3";

    private const long MinMultipartPartSize = 5L * 1024 * 1024;

    public static void AddProvider(
        IServiceCollection services, IConfiguration providerConfiguration)
    {
        services.AddOptionsWithValidateOnStart<S3StorageConfiguration>()
            .Bind(providerConfiguration)
            .ValidateDataAnnotations()
            .Validate(c =>
                c.Multipart.MaxSupportedPartSize >= MinMultipartPartSize,
                "S3 multipart maximum supported part size must be at least 5 MiB.")
            .Validate(
                o => o.Multipart.MaxSupportedPartCount > 0,
                "S3 multipart maximum supported part count must be greater than zero.");

        services.AddKeyedSingleton<IAmazonS3>(S3ClientKind.Regular, CreateClient);
        services.AddKeyedSingleton<IAmazonS3>(S3ClientKind.RetriesDisabled, CreateClient);

        services.AddSingleton<IStorageProvider, S3StorageProvider>();
    }

    private static AmazonS3Client CreateClient(IServiceProvider sp, object? key)
    {
        var config = sp
            .GetRequiredService<IOptions<S3StorageConfiguration>>()
            .Value;

        var s3ClientConfig = new AmazonS3Config
        {
            EndpointProvider =
                new NonStandardBucketNameEndpointProvider(),

            ForcePathStyle = config.ForcePathStyle,
            ServiceURL = config.ServiceUrl,

            RequestChecksumCalculation =
                   config.RequestChecksumCalculationEnabled
                       ? RequestChecksumCalculation.WHEN_SUPPORTED
                       : RequestChecksumCalculation.WHEN_REQUIRED,

            ResponseChecksumValidation =
                   config.ResponseChecksumCalculationEnabled
                       ? ResponseChecksumValidation.WHEN_SUPPORTED
                       : ResponseChecksumValidation.WHEN_REQUIRED
        };

        if (key is S3ClientKind.RetriesDisabled)
        {
            s3ClientConfig.MaxErrorRetry = 0;
            s3ClientConfig.MaxStaleConnectionRetries = 0;
        }

        return new AmazonS3Client(
            config.AccessKey, config.SecretKey, s3ClientConfig);
    }
}
