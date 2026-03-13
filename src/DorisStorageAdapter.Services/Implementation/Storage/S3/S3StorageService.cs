using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using DorisStorageAdapter.Helpers;
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

internal sealed class S3StorageService(
    IAmazonS3 client,
    IOptions<S3StorageServiceConfiguration> configuration) : IStorageService
{
    private readonly IAmazonS3 client = client;
    private readonly S3StorageServiceConfiguration configuration = configuration.Value;

    public async Task<StorageFileBaseMetadata> StoreAsync(
        string filePath,
        Stream data,
        long size,
        string? contentType,
        CancellationToken cancellationToken)
    {
        using var utility = new TransferUtility(client, new()
        {
            MinSizeBeforePartUpload = configuration.MultiPartUploadThreshold
        });

        var request = new TransferUtilityUploadRequest
        {
            AutoCloseStream = false,
            AutoResetStreamPosition = false,
            BucketName = configuration.BucketName,
            Key = filePath,

            InputStream = size == 0
                // Using Stream.Null when size is 0 is a workaround to make sure
                // that TransferUtility does not read synchronously from data, which
                // (for some reason) happens if the stream is empty. Trying to read synchrounously
                // triggers an ASP.NET core error unless AllowSynchronousIO is set to true.
                ? Stream.Null

                /// In order for TransferUtility to support multipart uploading
                /// without buffering each part in memory, InputStream must report Length
                /// and be seekable. Buffering should be avoided since it means that
                /// the value of configuration.MultiPartUploadChunkSize directly affects
                /// memory usage.
                /// 
                /// To make data.Stream seem seekable it is wrapped in a FakeSeekableStream. 
                /// Seeking is only actually used by TransferUtility when retrying a failed upload,
                /// so retries are disabled in S3StorageServiceConfigurer to avoid seeking here.
                : new FakeSeekableStream(data, size),

            PartSize = configuration.MultiPartUploadChunkSize
        };

        await utility.UploadAsync(request, cancellationToken);

        return new(
            ContentType: null,
            DateCreated: null,
            // DateTime.UtcNow is an approximation.
            // Must call GetObjectMetadataAsync to get the actual LastModified value,
            // which is arguably unnecessary overhead.
            DateModified: DateTime.UtcNow);
    }

    public async Task DeleteAsync(string filePath, CancellationToken cancellationToken)
    {
        await client.DeleteObjectAsync(new()
        {
            BucketName = configuration.BucketName,
            Key = filePath
        },
        cancellationToken);
    }

    public async Task<StorageFileMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.GetObjectMetadataAsync(new()
            {
                BucketName = configuration.BucketName,
                Key = filePath
            },
            cancellationToken);

            return new(
                ContentType: null,
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

    public async Task<StorageFileData?> GetDataAsync(string filePath, StorageByteRange? byteRange, CancellationToken cancellationToken)
    {
        try
        {
            var request = new GetObjectRequest()
            {
                BucketName = configuration.BucketName,
                Key = filePath
            };

            if (byteRange != null)
            {
                request.ByteRange = new(byteRange.ToHttpRangeValue());
            }

            var response = await client.GetObjectAsync(request, cancellationToken);

            return new(
                ContentType: null,
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
                    response = await client.GetObjectMetadataAsync(new()
                    {
                        BucketName = configuration.BucketName,
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
                    ContentType: null,
                    Size: response.ContentLength,
                    Stream: Stream.Null,
                    StreamLength: 0);
            }

            throw;
        }
    }

    public async IAsyncEnumerable<StorageFileMetadata> ListAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var paginator = client.Paginators.ListObjectsV2(new()
        {
            BucketName = configuration.BucketName,
            Prefix = path
        });

        await foreach (var file in paginator.S3Objects.WithCancellation(cancellationToken))
        {
            yield return new(
                ContentType: null,
                DateCreated: null,
                DateModified: file.LastModified?.ToUniversalTime(),
                Path: file.Key,
                Size: file.Size.GetValueOrDefault());
        }
    }
}
