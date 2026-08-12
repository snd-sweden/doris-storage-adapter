using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using DorisStorageAdapter.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Storage.S3;

internal sealed class S3StorageProvider(
    [FromKeyedServices(S3ClientKind.Regular)] IAmazonS3 client,
    [FromKeyedServices(S3ClientKind.RetriesDisabled)] IAmazonS3 retriesDisabledClient,
    IOptions<S3StorageConfiguration> configuration) : IStorageProvider
{
    private readonly IAmazonS3 _client = client;
    private readonly IAmazonS3 _retriesDisabledClient = retriesDisabledClient;
    private readonly S3StorageConfiguration _configuration = configuration.Value;

    private const int MultipartThreshold = 128 * 1024 * 1024;
    private const int PreferredPartSize = 64 * 1024 * 1024;

    public async Task StoreAsync(
        string filePath,
        Stream data,
        long size,
        CancellationToken cancellationToken)
    {
        if (size < MultipartThreshold)
        {
            await PutObjectAsync(
                filePath,
                data,
                size,
                cancellationToken);
        }
        else
        {
            await MultipartUploadAsync(
                filePath,
                data,
                size,
                cancellationToken);
        }
    }

    private Task<PutObjectResponse> PutObjectAsync(
        string filePath,
        Stream data,
        long size,
        CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            AutoCloseStream = false,
            AutoResetStreamPosition = false,
            BucketName = _configuration.BucketName,
            InputStream = size == 0 ? Stream.Null : data,
            Key = filePath
        };

        request.Headers.ContentLength = size;

        return _client.PutObjectAsync(request, cancellationToken);
    }

    private Task MultipartUploadAsync(
        string filePath,
        Stream data,
        long size,
        CancellationToken cancellationToken)
    {
        using var utility = new TransferUtility(_retriesDisabledClient, new()
        {
            MinSizeBeforePartUpload = MultipartThreshold
        });

        var request = new TransferUtilityUploadRequest
        {
            AutoCloseStream = false,
            AutoResetStreamPosition = false,
            BucketName = _configuration.BucketName,
            Key = filePath,

            InputStream = size == 0
                // Using Stream.Null when size is 0 is a workaround to make sure
                // that TransferUtility does not read synchronously from data, which
                // (for some reason) happens if the stream is empty.
                ? Stream.Null

                // In order for TransferUtility to support multipart uploading
                // without buffering each part in memory, InputStream must report Length
                // and be seekable. Buffering is avoided since it means that the calculated
                // part size affects memory usage.
                // 
                // To make data.Stream seem seekable it is wrapped in a VirtualSeekableStream. 
                // Seeking is only actually used by TransferUtility when retrying,
                // so the _retriesDisabledClient is used which has retries disabled.
                // Should TransferUtility try to seek anyway (e.g. because of changes in
                // the SDK implementation) an exception is thrown so that operation
                // fails fast and no data corruption can occur.
                : new VirtualSeekableStream(
                    data, size, VirtualSeekableStreamMode.ThrowOnSeek, leaveOpen: true),

            PartSize = CalculatePartSize(size)
        };

        return utility.UploadAsync(request, cancellationToken);
    }

    private long CalculatePartSize(long size)
    {
        int maxPartCount = _configuration.Multipart.MaxSupportedPartCount;
        long maxPartSize = _configuration.Multipart.MaxSupportedPartSize;

        // Smallest part size that can represent the object without exceeding maxPartCount.
        long requiredPartSize = 
            size / maxPartCount + 
            (size % maxPartCount == 0 ? 0 : 1);

        if (requiredPartSize > maxPartSize)
        {
            throw new InvalidOperationException(
                $"Object of {size} bytes cannot be multipart uploaded with " +
                $"a maximum part size of {maxPartSize} bytes and {maxPartCount} parts.");
        }

        // Start from a preferred part size and double it only when necessary to stay within
        // the part-count limit. Clamp to the backend's configured maximum part size.
        long partSize = Math.Min(PreferredPartSize, maxPartSize);

        while (partSize < requiredPartSize)
        {
            if (partSize > maxPartSize / 2)
            {
                partSize = maxPartSize;
                break;
            }

            partSize *= 2;
        }

        return partSize;
    }

    public async Task DeleteAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await _client.DeleteObjectAsync(new()
        {
            BucketName = _configuration.BucketName,
            Key = filePath
        },
        cancellationToken);
    }

    public async Task<StorageFileMetadata?> GetMetadataAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetObjectMetadataAsync(new()
            {
                BucketName = _configuration.BucketName,
                Key = filePath
            },
            cancellationToken);

            return new(
                DateCreated: null,
                DateModified: response.LastModified?.ToUniversalTime(),
                Path: filePath,
                Size: response.ContentLength);
        }
        catch (AmazonS3Exception e)
        {
            if (e.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw;
        }
    }

    public async Task<StorageFileData?> GetDataAsync(
        string filePath,
        StorageByteRange? byteRange,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new GetObjectRequest()
            {
                BucketName = _configuration.BucketName,
                Key = filePath
            };

            if (byteRange != null)
            {
                request.ByteRange = new(byteRange.ToHttpRangeValue());
            }

            var response = await _client.GetObjectAsync(request, cancellationToken);

            return new(
                Size:
                    response.HttpStatusCode == HttpStatusCode.PartialContent
                        ? ContentRangeHeaderValue.TryParse(response.ContentRange, out var contentRange)
                            ? contentRange.Length.GetValueOrDefault()
                            : 0
                        : response.ContentLength,
                Stream: response.ResponseStream,
                StreamLength: response.ContentLength);
        }
        catch (AmazonS3Exception e)
        {
            if (e.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (e.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // For some (stupid) reason, the respone HTTP headers can not be accessed here,
                // which means that the Content-Range header can not be used to get the
                // TotalLength value. Resort to issuing a new request to S3 to get the length.

                GetObjectMetadataResponse response;
                try
                {
                    response = await _client.GetObjectMetadataAsync(new()
                    {
                        BucketName = _configuration.BucketName,
                        Key = filePath
                    },
                    cancellationToken);
                }
                catch (AmazonS3Exception e2)
                {
                    if (e2.StatusCode == HttpStatusCode.NotFound)
                    {
                        return null;
                    }

                    throw;
                }

                // Return an empty stream to indicate that the
                // requested range was not satisfiable.
                return new(
                    Size: response.ContentLength,
                    Stream: Stream.Null,
                    StreamLength: 0);
            }

            throw;
        }
    }

    public async IAsyncEnumerable<StorageFileMetadata> ListAsync(
        string path,
        bool recursive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var paginator = _client.Paginators.ListObjectsV2(new()
        {
            BucketName = _configuration.BucketName,
            Delimiter = recursive ? null : "/",
            Prefix = path
        });

        await foreach (var file in paginator.S3Objects.WithCancellation(cancellationToken))
        {
            yield return new(
                DateCreated: null,
                DateModified: file.LastModified?.ToUniversalTime(),
                Path: file.Key,
                Size: file.Size.GetValueOrDefault());
        }
    }
}
