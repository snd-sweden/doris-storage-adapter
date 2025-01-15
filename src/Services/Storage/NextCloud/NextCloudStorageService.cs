using DorisStorageAdapter.Services.Lock;
using Microsoft.Extensions.Options;
using Nerdbank.Streams;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using WebDav;

namespace DorisStorageAdapter.Services.Storage.NextCloud;

internal sealed class NextCloudStorageService : IStorageService
{
    private readonly ILockService lockService;
    private readonly IWebDavClient webDavClient;
    private readonly NextCloudStorageServiceConfiguration configuration;

    private readonly Uri storageBaseUri;
    private readonly Uri chunkedUploadBaseUri;
    private readonly Uri tmpFileBaseUri;

    public NextCloudStorageService(
        IWebDavClient webDavClient,
        IOptions<NextCloudStorageServiceConfiguration> configuration,
        ILockService lockService)
    {
        this.webDavClient = webDavClient;
        this.configuration = configuration.Value;
        this.lockService = lockService;

        var filesBaseUri = GetUri(this.configuration.BaseUrl, $"remote.php/dav/files/{this.configuration.User}/");

        storageBaseUri = GetUri(filesBaseUri, $"{this.configuration.BasePath}" +
            (this.configuration.BasePath.EndsWith('/') ? "" : '/'));

        tmpFileBaseUri = GetUri(filesBaseUri, $"{this.configuration.TempFilePath}" +
             (this.configuration.TempFilePath.EndsWith('/') ? "" : '/'));

        chunkedUploadBaseUri = GetUri(this.configuration.BaseUrl, $"remote.php/dav/uploads/{this.configuration.User}/");
    }

    public async Task<StorageServiceFileBase> StoreFile(string filePath, FileData data, CancellationToken cancellationToken)
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
                EnsureSuccessStatusCode(await webDavClient.PutFile(tempFileUri, data.Stream, new PutFileParameters()
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
            // Add Destination header to all calls to ensure we use v2 of NextCloud's chunked upload API.
            var destinationHeader = KeyValuePair.Create("Destination", fileUri.AbsoluteUri);
            long now;

            try
            {
                EnsureSuccessStatusCode(await webDavClient.Mkcol(uri, new()
                {
                    CancellationToken = cancellationToken,
                    Headers = [destinationHeader]
                }));

                long bytesLeft = data.Length;
                int chunk = 1;

                do
                {
                    long bytesToRead = Math.Min(configuration.ChunkedUploadChunkSize, bytesLeft);

                    EnsureSuccessStatusCode(await webDavClient.PutFile(
                        new Uri(uri, chunk.ToString(CultureInfo.InvariantCulture)),
                        data.Stream.ReadSlice(bytesToRead),
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

            if (data.Length > configuration.ChunkedUploadThreshold)
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

    public async Task DeleteFile(string filePath, CancellationToken cancellationToken)
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


    public Task<StorageServiceFile?> GetFileMetadata(string filePath, CancellationToken cancellationToken)
    {
        // Behöver byggas med propfind

        return Task.FromResult<StorageServiceFile?>(null);
    }

    public async Task<FileData?> GetFileData(string filePath, CancellationToken cancellationToken)
    {
        var response = await webDavClient.GetFileResponse(GetWebDavFileUri(filePath), true, new()
        {
            CancellationToken = cancellationToken
        });

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return new(
            Stream: await response.Content.ReadAsStreamAsync(cancellationToken),
            Length: response.Content.Headers.ContentLength!.Value,
            ContentType: response.Content.Headers.ContentType?.MediaType);
    }

    public async IAsyncEnumerable<StorageServiceFile> ListFiles(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Task<PropfindResponse> DoPropFind(Uri uri, CancellationToken cancellationToken) =>
            webDavClient.Propfind(uri, new PropfindParameters
            {
                ApplyTo = ApplyTo.Propfind.ResourceAndAncestors,
                Namespaces = [new("d", "DAV:")],
                CustomProperties = [
                    XName.Get("getlastmodified", "DAV:"),
                    XName.Get("getcontentlength", "DAV:"),
                    XName.Get("resourcetype", "DAV:")
                ],
                RequestType = PropfindRequestType.NamedProperties,
                CancellationToken = cancellationToken
            });

        var baseUri = GetWebDavFileUri(path);
        var response = await DoPropFind(baseUri, cancellationToken);

        if (NotFound(response))
        {
            if (path.EndsWith('/'))
            {
                // Path denotes a directory, no use trying with parent directory.
                yield break;
            }

            // Try with parent directory.

            baseUri = GetParentUri(baseUri);
            response = await DoPropFind(baseUri, cancellationToken);

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
                    Length: file.ContentLength.GetValueOrDefault());
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

    private Task<IDisposable> LockPath(Uri directoryUri, CancellationToken cancellationToken) =>
        lockService.LockPath(GetLockPath(directoryUri), cancellationToken);

    private async Task CreateDirectory(Uri directoryUri, CancellationToken cancellationToken)
    {
        using (await LockPath(directoryUri, cancellationToken))
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
        using (await LockPath(directoryUri, cancellationToken))
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
