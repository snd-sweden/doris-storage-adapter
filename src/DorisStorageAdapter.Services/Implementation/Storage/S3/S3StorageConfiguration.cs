using System.ComponentModel.DataAnnotations;

namespace DorisStorageAdapter.Services.Implementation.Storage.S3;

internal sealed record S3StorageConfiguration
{
    [Required]
    [Url]
    public required string ServiceUrl { get; init; }
    [Required]
    public required string BucketName { get; init; }
    [Required]
    public required string AccessKey { get; init; }
    [Required]
    public required string SecretKey { get; init; }

    public bool ForcePathStyle { get; init; } = true;
    public MultipartConfiguration Multipart { get; init; } = new();
    public bool RequestChecksumCalculationEnabled { get; init; } = true;
    public bool ResponseChecksumCalculationEnabled { get; init; } = true;

    public sealed record MultipartConfiguration
    {
        public int MaxSupportedPartCount { get; init; } = 10_000;
        public long MaxSupportedPartSize { get; init; } = 5L * 1024 * 1024 * 1024;
    }
}
