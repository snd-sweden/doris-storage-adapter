using Microsoft.Extensions.Options;
using Nerdbank.Streams;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using WebDav;

namespace DorisStorageAdapter.Services.Implementation.Storage.NextCloud;

internal sealed class NextCloudStorageProvider : IStorageProvider
{
    private readonly IStorageLockProvider _lockProvider;
    private readonly IWebDavClient _webDavClient;
    private readonly NextCloudStorageConfiguration _configuration;

    private readonly Uri _storageBaseUri;
    private readonly Uri _chunkedUploadBaseUri;
    private readonly Uri _tmpFileBaseUri;

    private const string DavNamespaceName = "DAV:";
    private static readonly XName _getLastModifiedProperty = XName.Get("getlastmodified", DavNamespaceName);
    private static readonly XName _getContentLengthProperty = XName.Get("getcontentlength", DavNamespaceName);
    private static readonly XName _resourceTypeProperty = XName.Get("resourcetype", DavNamespaceName);

    public NextCloudStorageProvider(
        IWebDavClient webDavClient,
        IOptions<NextCloudStorageConfiguration> configuration,
        IStorageLockProvider pathLock)
    {
        _webDavClient = webDavClient;
        _configuration = configuration.Value;
        _lockProvider = pathLock;

        var filesBaseUri = GetUri(_configuration.BaseUrl, $"remote.php/dav/files/{_configuration.User}/");

        _storageBaseUri = GetUri(filesBaseUri, $"{_configuration.BasePath}" +
            (_configuration.BasePath.EndsWith('/') ? "" : '/'));

        _tmpFileBaseUri = GetUri(filesBaseUri, $"{_configuration.TempFilePath}" +
             (_configuration.TempFilePath.EndsWith('/') ? "" : '/'));

        _chunkedUploadBaseUri = GetUri(_configuration.BaseUrl, $"remote.php/dav/uploads/{_configuration.User}/");
    }

    public async Task<StorageFileBaseMetadata> StoreAsync(
        string filePath,
        Stream data,
        long size,
        string? contentType,
        CancellationToken cancellationToken)
    {
        long GetNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var fileUri = GetWebDavFileUri(filePath);
        var directoryUri = GetParentUri(fileUri);

        async Task<long> DoUploadAsync()
        {
            var tempFileUri = new Uri(_tmpFileBaseUri, Guid.NewGuid().ToString());
            var now = GetNow();

            try
            {
                EnsureSuccessStatusCode(await _webDavClient.PutFile(tempFileUri, data, new PutFileParameters()
                {
                    CancellationToken = cancellationToken,
                    Headers = [new("X-OC-MTime", now.ToString(CultureInfo.InvariantCulture))] // Explicitly sets last modified date
                }));

                await using (await AcquireDirectoryLockAsync(directoryUri, cancellationToken))
                {
                    await CreateDirectoryAsync(directoryUri, cancellationToken);

                    EnsureSuccessStatusCode(await _webDavClient.Move(tempFileUri, fileUri, new()
                    {
                        CancellationToken = cancellationToken,
                        Overwrite = true
                    }));
                }
            }
            catch
            {
                // Cancelled or failed, try to clean up.
                try
                {
                    await _webDavClient.Delete(tempFileUri, new()
                    {
                        CancellationToken = CancellationToken.None
                    });
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch { }
#pragma warning restore CA1031

                throw;
            }

            return now;
        }

        async Task<long> DoChunkedUploadAsync()
        {
            var uri = new Uri(_chunkedUploadBaseUri, "doris-storage-adapter-" + Guid.NewGuid().ToString() + '/');
            // Add Destination header to all calls to ensure the v2 version of NextCloud's chunked upload API is used.
            var destinationHeader = KeyValuePair.Create("Destination", fileUri.AbsoluteUri);
            long now;

            try
            {
                EnsureSuccessStatusCode(await _webDavClient.Mkcol(uri, new()
                {
                    CancellationToken = cancellationToken,
                    Headers = [destinationHeader]
                }));

                long bytesLeft = size;
                int chunk = 1;

                do
                {
                    long bytesToRead = Math.Min(_configuration.ChunkedUploadChunkSize, bytesLeft);

                    EnsureSuccessStatusCode(await _webDavClient.PutFile(
                        new Uri(uri, chunk.ToString(CultureInfo.InvariantCulture)),
                        data.ReadSlice(bytesToRead),
                        new PutFileParameters
                        {
                            CancellationToken = cancellationToken,
                            Headers = [destinationHeader]
                        }));

                    bytesLeft -= bytesToRead;
                    chunk++;
                }
                while (bytesLeft > 0);

                now = GetNow();

                await using (await AcquireDirectoryLockAsync(directoryUri, cancellationToken))
                {
                    await CreateDirectoryAsync(directoryUri, cancellationToken);

                    EnsureSuccessStatusCode(await _webDavClient.Move(new Uri(uri, ".file"), fileUri, new()
                    {
                        CancellationToken = cancellationToken,
                        Headers = [
                        destinationHeader,
                        new("X-OC-MTime", now.ToString(CultureInfo.InvariantCulture)) // Explicitly sets last modified date
                    ]
                    }));
                }
            }
            catch
            {
                // Cancelled or failed, try to clean up.
                try
                {
                    var response = await _webDavClient.Delete(uri, new()
                    {
                        CancellationToken = CancellationToken.None
                    });
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch { }
#pragma warning restore CA1031

                throw;
            }

            return now;
        }

        long now;

        try
        {
            if (size > _configuration.ChunkedUploadThreshold)
            {
                now = await DoChunkedUploadAsync();
            }
            else
            {
                now = await DoUploadAsync();
            }
        }
        catch
        {
            // Cancelled or failed, try to clean up.
            try
            {
                await DeleteEmptyDirectoriesAsync(directoryUri, CancellationToken.None);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch { }
#pragma warning restore CA1031

            throw;
        }

        return new(
            ContentType: null,
            DateCreated: null,
            DateModified: DateTimeOffset.FromUnixTimeSeconds(now).UtcDateTime);
    }

    public async Task DeleteAsync(
        string filePath, 
        CancellationToken cancellationToken)
    {
        var fileUri = GetWebDavFileUri(filePath);

        var response = await _webDavClient.Delete(fileUri, new()
        {
            CancellationToken = cancellationToken
        });

        if (!NotFound(response))
        {
            EnsureSuccessStatusCode(response);
        }

        try
        {
            // Delete any empty subdirectories that result from deleting the file.
            await DeleteEmptyDirectoriesAsync(GetParentUri(fileUri), CancellationToken.None);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch
#pragma warning restore CA1031
        {
            // Ignore errors here since file has been successfully deleted
            // and deleting empty directories is not crucial.
        }
    }

    public async Task<StorageFileMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken)
    {
        var uri = GetWebDavFileUri(filePath);
        var response = await DoPropfindAsync(
            uri,
            ApplyTo.Propfind.ResourceOnly,
            [
                _getLastModifiedProperty,
                _getContentLengthProperty
            ],
            cancellationToken);

        if (NotFound(response) || response.Resources.Count == 0)
        {
            return null;
        }

        EnsureSuccessStatusCode(response);

        var resource = response.Resources.First();

        return new(
            ContentType: null,
            DateCreated: null,
            DateModified: resource.LastModifiedDate?.ToUniversalTime(),
            Path: filePath,
            Size: resource.ContentLength.GetValueOrDefault());
    }

    public async Task<StorageFileData?> GetDataAsync(
        string filePath, 
        StorageByteRange? byteRange, 
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<KeyValuePair<string, string>> headers =
            byteRange == null
                ? []
                : [new(HttpRequestHeader.Range.ToString(), byteRange.ToHttpRangeValue())];

        var uri = GetWebDavFileUri(filePath);

        var response = await _webDavClient.GetFileResponse(uri, true, new()
        {
            CancellationToken = cancellationToken,
            Headers = headers
        });

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable ||
            // NextCloud returns 206 with an invalid Content-Range for requests with "Range: bytes=-0"
            // when it should be returning 416. Explicitly check for that case here.
            byteRange is { From: null, To: 0 })
        {
            // Return an empty stream to indicate that the
            // requested range was not satisfiable.

            // NextCloud does not respond with valid Content-Range header,
            // resort to issuing a new request to get the length.

            var propFindResponse = await DoPropfindAsync(
                uri,
                ApplyTo.Propfind.ResourceOnly,
                [_getContentLengthProperty],
                cancellationToken);

            if (NotFound(propFindResponse) || propFindResponse.Resources.Count == 0)
            {
                return null;
            }

            EnsureSuccessStatusCode(propFindResponse);

            return new(
                ContentType: null,
                Size: propFindResponse.Resources.First().ContentLength!.Value,
                Stream: Stream.Null,
                StreamLength: 0);
        }

        response.EnsureSuccessStatusCode();

        long contentLength = response.Content.Headers.ContentLength!.Value;

        return new(
            ContentType: response.Content.Headers.ContentType?.MediaType,
            Size:
                response.StatusCode == HttpStatusCode.PartialContent
                    ? response.Content.Headers.ContentRange?.Length.GetValueOrDefault() ?? 0
                    : contentLength,
            Stream: await response.Content.ReadAsStreamAsync(cancellationToken),
            StreamLength: contentLength);
    }

    public async IAsyncEnumerable<StorageFileMetadata> ListAsync(
        string path,
        bool recursive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Task<PropfindResponse> DoPropfindAsync(Uri uri, CancellationToken cancellationToken) =>
            this.DoPropfindAsync(
                uri,
                recursive 
                    ? ApplyTo.Propfind.ResourceAndAncestors
                    : ApplyTo.Propfind.ResourceAndChildren,
                [
                    _getLastModifiedProperty,
                    _getContentLengthProperty,
                    _resourceTypeProperty
                ],
                cancellationToken);

        var baseUri = GetWebDavFileUri(path);
        var response = await DoPropfindAsync(baseUri, cancellationToken);

        if (NotFound(response))
        {
            if (path.EndsWith('/'))
            {
                // Path denotes a directory, no use trying with parent directory.
                yield break;
            }

            // Try with parent directory.

            baseUri = GetParentUri(baseUri);
            response = await DoPropfindAsync(baseUri, cancellationToken);

            if (NotFound(response))
            {
                yield break;
            }
        }

        EnsureSuccessStatusCode(response);

        foreach (var file in response.Resources.Where(r => !r.IsCollection))
        {
            string filePath = Uri.UnescapeDataString(_storageBaseUri.MakeRelativeUri(new Uri(_storageBaseUri, file.Uri)).ToString());

            if (filePath.StartsWith(path, StringComparison.Ordinal))
            {
                yield return new(
                    ContentType: null,
                    DateCreated: null,
                    DateModified: file.LastModifiedDate?.ToUniversalTime(),
                    Path: filePath,
                    Size: file.ContentLength.GetValueOrDefault());
            }
        }
    }

    private static string UrlEncodePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static Uri GetUri(Uri baseUri, string path) =>
        new(baseUri, UrlEncodePath(path));

    private Uri GetWebDavFileUri(string filePath) => GetUri(_storageBaseUri, filePath);

    private static Uri GetParentUri(Uri uri)
    {
        string absoluteUri = uri.AbsoluteUri;
        if (absoluteUri.EndsWith('/'))
        {
            absoluteUri = absoluteUri[..^1];
        }

        return new(absoluteUri[..(absoluteUri.LastIndexOf('/') + 1)]);
    }

    private Task<PropfindResponse> DoPropfindAsync(
        Uri uri,
        ApplyTo.Propfind applyTo,
        IReadOnlyCollection<XName> customProperties,
        CancellationToken cancellationToken) =>
        _webDavClient.Propfind(uri, new PropfindParameters
        {
            ApplyTo = applyTo,
            Namespaces = [new("d", DavNamespaceName)],
            CustomProperties = customProperties,
            RequestType = PropfindRequestType.NamedProperties,
            CancellationToken = cancellationToken
        });

    private async Task<bool> DirectoryExistsAsync(Uri uri, CancellationToken cancellationToken)
    {
        var response = await _webDavClient.Propfind(uri, new()
        {
            ApplyTo = ApplyTo.Propfind.ResourceOnly,
            RequestType = PropfindRequestType.NamedProperties,
            Namespaces = [new("d", "DAV:")],
            CustomProperties = [XName.Get("resourcetype", "DAV:")],
            CancellationToken = cancellationToken
        });

        if (NotFound(response))
        {
            return false;
        }

        EnsureSuccessStatusCode(response);

        return
            response.Resources.Count == 1 &&
            response.Resources.First().IsCollection;
    }

    /// <summary>
    /// Returns the root directory of the given directory
    /// to be used as lock name when creating/deleting directories.
    /// </summary>
    /// <param name="directoryUri">The directory to get lock name for.</param>
    /// <returns>The lock name (the root directory).</returns>
    private string GetDirectoryLockName(Uri directoryUri)
    {
        string relativePath =
            _storageBaseUri.MakeRelativeUri(directoryUri).ToString();

        // base/root itself
        if (string.IsNullOrEmpty(relativePath))
        {
            return "/";
        }

        int slashIndex = relativePath.IndexOf('/', StringComparison.Ordinal);

        if (slashIndex >= 0)
        {
            return "/" + relativePath[..slashIndex];
        }

        return "/" + relativePath;
    }

    private ValueTask<IAsyncDisposable> AcquireDirectoryLockAsync(
        Uri directoryUri, CancellationToken cancellationToken) =>
        _lockProvider.AcquireAsync(GetDirectoryLockName(directoryUri), cancellationToken);

    private async Task CreateDirectoryAsync(Uri directoryUri, CancellationToken cancellationToken)
    {
        var directoriesToCreate = new Stack<Uri>();

        while (!_storageBaseUri.Equals(directoryUri))
        {
            if (await DirectoryExistsAsync(directoryUri, cancellationToken))
            {
                break;
            }

            directoriesToCreate.Push(directoryUri);
            directoryUri = GetParentUri(directoryUri);
        }

        foreach (var directory in directoriesToCreate)
        {
            EnsureSuccessStatusCode(await _webDavClient.Mkcol(directory, new()
            {
                CancellationToken = cancellationToken
            }));
        }
    }

    private async Task DeleteEmptyDirectoriesAsync(Uri directoryUri, CancellationToken cancellationToken)
    {
        await using var _ = await AcquireDirectoryLockAsync(directoryUri, cancellationToken);

        while (!_storageBaseUri.Equals(directoryUri))
        {
            var response = await _webDavClient.Propfind(directoryUri, new()
            {
                ApplyTo = ApplyTo.Propfind.ResourceAndChildren,
                RequestType = PropfindRequestType.NamedProperties,
                Namespaces = [new("d", "DAV:")],
                CustomProperties = [XName.Get("resourcetype", "DAV:")],
                CancellationToken = cancellationToken
            });

            if (!NotFound(response))
            {
                EnsureSuccessStatusCode(response);

                if (response.Resources.Count == 1)
                {
                    EnsureSuccessStatusCode(await _webDavClient.Delete(directoryUri, new()
                    {
                        CancellationToken = cancellationToken
                    }));
                }
                else
                {
                    return;
                }
            }

            directoryUri = GetParentUri(directoryUri);
        }
    }

    private static bool NotFound<T>(T response) where T : WebDavResponse =>
        response.StatusCode == (int)HttpStatusCode.NotFound;

    private static T EnsureSuccessStatusCode<T>(T response) where T : WebDavResponse
    {
        if (!response.IsSuccessful)
        {
            throw new HttpRequestException(
                "Response status code does not indicate success", null, (HttpStatusCode)response.StatusCode);
        }

        return response;
    }
}
