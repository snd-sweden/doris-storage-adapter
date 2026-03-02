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

internal sealed class NextCloudStorageService : IStorageService
{
    private readonly IStoragePathLock pathLock;
    private readonly IWebDavClient webDavClient;
    private readonly NextCloudStorageServiceConfiguration configuration;

    private readonly Uri storageBaseUri;
    private readonly Uri chunkedUploadBaseUri;
    private readonly Uri tmpFileBaseUri;

    private const string davNamespaceName = "DAV:";
    private static readonly XName getLastModifiedProperty = XName.Get("getlastmodified", davNamespaceName);
    private static readonly XName getContentLengthProperty = XName.Get("getcontentlength", davNamespaceName);
    private static readonly XName resourceTypeProperty = XName.Get("resourcetype", davNamespaceName);

    public NextCloudStorageService(
        IWebDavClient webDavClient,
        IOptions<NextCloudStorageServiceConfiguration> configuration,
        IStoragePathLock pathLock)
    {
        this.webDavClient = webDavClient;
        this.configuration = configuration.Value;
        this.pathLock = pathLock;

        var filesBaseUri = GetUri(this.configuration.BaseUrl, $"remote.php/dav/files/{this.configuration.User}/");

        storageBaseUri = GetUri(filesBaseUri, $"{this.configuration.BasePath}" +
            (this.configuration.BasePath.EndsWith('/') ? "" : '/'));

        tmpFileBaseUri = GetUri(filesBaseUri, $"{this.configuration.TempFilePath}" +
             (this.configuration.TempFilePath.EndsWith('/') ? "" : '/'));

        chunkedUploadBaseUri = GetUri(this.configuration.BaseUrl, $"remote.php/dav/uploads/{this.configuration.User}/");
    }

    public async Task<StorageFileBaseMetadata> Store(
        string filePath,
        Stream data,
        long size,
        string? contentType,
        CancellationToken cancellationToken)
    {
        long GetNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var fileUri = GetWebDavFileUri(filePath);
        var directoryUri = GetParentUri(fileUri);

        async Task<long> DoUpload()
        {
            var tempFileUri = new Uri(tmpFileBaseUri, Guid.NewGuid().ToString());
            var now = GetNow();

            try
            {
                EnsureSuccessStatusCode(await webDavClient.PutFile(tempFileUri, data, new PutFileParameters()
                {
                    CancellationToken = cancellationToken,
                    Headers = [new("X-OC-MTime", now.ToString(CultureInfo.InvariantCulture))] // Explicitly sets last modified date
                }));

                EnsureSuccessStatusCode(await webDavClient.Move(tempFileUri, fileUri, new()
                {
                    CancellationToken = cancellationToken,
                    Overwrite = true
                }));
            }
            catch
            {
                // Cancelled or failed, try to clean up.
                try
                {
                    await webDavClient.Delete(tempFileUri, new()
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

        async Task<long> DoChunkedUpload()
        {
            var uri = new Uri(chunkedUploadBaseUri, "doris-storage-adapter-" + Guid.NewGuid().ToString() + '/');
            // Add Destination header to all calls to ensure the v2 version of NextCloud's chunked upload API is used.
            var destinationHeader = KeyValuePair.Create("Destination", fileUri.AbsoluteUri);
            long now;

            try
            {
                EnsureSuccessStatusCode(await webDavClient.Mkcol(uri, new()
                {
                    CancellationToken = cancellationToken,
                    Headers = [destinationHeader]
                }));

                long bytesLeft = size;
                int chunk = 1;

                do
                {
                    long bytesToRead = Math.Min(configuration.ChunkedUploadChunkSize, bytesLeft);

                    EnsureSuccessStatusCode(await webDavClient.PutFile(
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

                EnsureSuccessStatusCode(await webDavClient.Move(new Uri(uri, ".file"), fileUri, new()
                {
                    CancellationToken = cancellationToken,
                    Headers = [
                        destinationHeader,
                        new("X-OC-MTime", now.ToString(CultureInfo.InvariantCulture)) // Explicitly sets last modified date
                    ]
                }));
            }
            catch
            {
                // Cancelled or failed, try to clean up.
                try
                {
                    var response = await webDavClient.Delete(uri, new()
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
            await CreateDirectory(directoryUri, cancellationToken);

            if (size > configuration.ChunkedUploadThreshold)
            {
                now = await DoChunkedUpload();
            }
            else
            {
                now = await DoUpload();
            }
        }
        catch
        {
            // Cancelled or failed, try to clean up.
            try
            {
                await DeleteEmptyDirectories(directoryUri, CancellationToken.None);
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

    public async Task Delete(string filePath, CancellationToken cancellationToken)
    {
        var fileUri = GetWebDavFileUri(filePath);

        var response = await webDavClient.Delete(fileUri, new()
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
            await DeleteEmptyDirectories(GetParentUri(fileUri), CancellationToken.None);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch
#pragma warning restore CA1031
        {
            // Ignore errors here since file has been successfully deleted
            // and deleting empty directories is not crucial.
        }
    }

    public async Task<StorageFileMetadata?> GetMetadata(string filePath, CancellationToken cancellationToken)
    {
        var uri = GetWebDavFileUri(filePath);
        var response = await DoPropfind(
            uri,
            ApplyTo.Propfind.ResourceOnly,
            [
                getLastModifiedProperty,
                getContentLengthProperty
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

    public async Task<StorageFileData?> GetData(string filePath, StorageByteRange? byteRange, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<KeyValuePair<string, string>> headers =
            byteRange == null
                ? []
                : [new(HttpRequestHeader.Range.ToString(), byteRange.ToHttpRangeValue())];

        var uri = GetWebDavFileUri(filePath);

        var response = await webDavClient.GetFileResponse(uri, true, new()
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

            var propFindResponse = await DoPropfind(
                uri,
                ApplyTo.Propfind.ResourceOnly,
                [getContentLengthProperty],
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

    public async IAsyncEnumerable<StorageFileMetadata> List(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Task<PropfindResponse> DoPropfind(Uri uri, CancellationToken cancellationToken) =>
            this.DoPropfind(
                uri,
                ApplyTo.Propfind.ResourceAndAncestors,
                [
                    getLastModifiedProperty,
                    getContentLengthProperty,
                    resourceTypeProperty
                ],
                cancellationToken);

        var baseUri = GetWebDavFileUri(path);
        var response = await DoPropfind(baseUri, cancellationToken);

        if (NotFound(response))
        {
            if (path.EndsWith('/'))
            {
                // Path denotes a directory, no use trying with parent directory.
                yield break;
            }

            // Try with parent directory.

            baseUri = GetParentUri(baseUri);
            response = await DoPropfind(baseUri, cancellationToken);

            if (NotFound(response))
            {
                yield break;
            }
        }

        EnsureSuccessStatusCode(response);

        foreach (var file in response.Resources.Where(r => !r.IsCollection))
        {
            string filePath = DecodeUrlEncodedPath(storageBaseUri.MakeRelativeUri(new Uri(storageBaseUri, file.Uri)).ToString());

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

    private static string DecodeUrlEncodedPath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.UnescapeDataString));

    private static Uri GetUri(Uri baseUri, string path) =>
        new(baseUri, UrlEncodePath(path));

    private Uri GetWebDavFileUri(string filePath) => GetUri(storageBaseUri, filePath);

    private static Uri GetParentUri(Uri uri)
    {
        string absoluteUri = uri.AbsoluteUri;
        if (absoluteUri.EndsWith('/'))
        {
            absoluteUri = absoluteUri[..^1];
        }

        return new(absoluteUri[..(absoluteUri.LastIndexOf('/') + 1)]);
    }

    private Task<PropfindResponse> DoPropfind(
        Uri uri,
        ApplyTo.Propfind applyTo,
        IReadOnlyCollection<XName> customProperties,
        CancellationToken cancellationToken) =>
        webDavClient.Propfind(uri, new PropfindParameters
        {
            ApplyTo = applyTo,
            Namespaces = [new("d", davNamespaceName)],
            CustomProperties = customProperties,
            RequestType = PropfindRequestType.NamedProperties,
            CancellationToken = cancellationToken
        });

    private async Task<bool> DirectoryExists(Uri uri, CancellationToken cancellationToken)
    {
        var response = await webDavClient.Propfind(uri, new()
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
    /// to be used as lock path when creating/deleting directories.
    /// </summary>
    /// <param name="directoryUri">The directory to get lock path for.</param>
    /// <returns>The lock path (the root directory).</returns>
    private string GetLockPath(Uri directoryUri)
    {
        var relativeUri = storageBaseUri.MakeRelativeUri(directoryUri).ToString();

        int index = relativeUri.IndexOf('/', StringComparison.Ordinal) + 1;
        if (index > 0)
        {
            return relativeUri[..index];
        }

        return relativeUri;
    }

    private ValueTask<IAsyncDisposable> LockPath(Uri directoryUri, CancellationToken cancellationToken) =>
        pathLock.LockPath(GetLockPath(directoryUri), cancellationToken);

    private async Task CreateDirectory(Uri directoryUri, CancellationToken cancellationToken)
    {
        await using (await LockPath(directoryUri, cancellationToken))
        {
            var directoriesToCreate = new Stack<Uri>();

            while (!storageBaseUri.Equals(directoryUri))
            {
                if (await DirectoryExists(directoryUri, cancellationToken))
                {
                    break;
                }

                directoriesToCreate.Push(directoryUri);
                directoryUri = GetParentUri(directoryUri);
            }

            foreach (var directory in directoriesToCreate)
            {
                EnsureSuccessStatusCode(await webDavClient.Mkcol(directory, new()
                {
                    CancellationToken = cancellationToken
                }));
            }
        }
    }

    private async Task DeleteEmptyDirectories(Uri directoryUri, CancellationToken cancellationToken)
    {
        await using (await LockPath(directoryUri, cancellationToken))
        {
            while (!storageBaseUri.Equals(directoryUri))
            {
                var response = await webDavClient.Propfind(directoryUri, new()
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
                        EnsureSuccessStatusCode(await webDavClient.Delete(directoryUri, new()
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
