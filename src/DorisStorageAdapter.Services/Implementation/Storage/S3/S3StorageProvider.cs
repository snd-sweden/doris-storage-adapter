using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using DorisStorageAdapter.Common;
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
    IAmazonS3 client,
    IOptions<S3StorageConfiguration> configuration) : IStorageProvider
{
    private readonly IAmazonS3 _client = client;
    private readonly S3StorageConfiguration _configuration = configuration.Value;

    public async Task StoreAsync(
        string filePath,
        Stream data,
        long size,
        CancellationToken cancellationToken)
    {
        using var utility = new TransferUtility(_client, new()
        {
            MinSizeBeforePartUpload = _configuration.MultiPartUploadThreshold
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
                /// so retries are disabled in S3StorageProviderConfigurer to avoid seeking here.
                : new FakeSeekableStream(data, size),

            PartSize = _configuration.MultiPartUploadChunkSize
        };

        await utility.UploadAsync(request, cancellationToken);
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
