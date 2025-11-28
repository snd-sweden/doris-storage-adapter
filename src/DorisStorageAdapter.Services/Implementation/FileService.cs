using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.BagIt;
using DorisStorageAdapter.Services.Implementation.BagIt.Fetch;
using DorisStorageAdapter.Services.Implementation.BagIt.Info;
using DorisStorageAdapter.Services.Implementation.BagIt.Manifest;
using DorisStorageAdapter.Services.Implementation.Lock;
using DorisStorageAdapter.Services.Implementation.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation;

internal sealed class FileService(
    IStorageService storageService,
    ILockService lockService,
    MetadataService metadataService) : IFileService
{
    private readonly IStorageService storageService = storageService;
    private readonly ILockService lockService = lockService;
    private readonly MetadataService metadataService = metadataService;

    public async Task<FileMetadata> Store(
        DatasetVersion datasetVersion,
        FileType type,
        string filePath,
        Stream data,
        long size,
        string? contentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(data);

        filePath = GetFilePathOrThrow(type, filePath);
        FileMetadata? result = default;

        bool lockSuccessful = false;
        await lockService.TryLockDatasetVersionShared(datasetVersion, async () =>
        {
            string fullFilePath = Paths.GetFullFilePath(datasetVersion, filePath);

            lockSuccessful = await lockService.TryLockPath(fullFilePath, async () =>
            {
                await ThrowIfHasBeenPublished(datasetVersion, cancellationToken);
                result = await StoreImpl(
                    datasetVersion,
                    type,
                    filePath,
                    fullFilePath,
                    data,
                    size,
                    contentType,
                    cancellationToken);
            },
            cancellationToken);

        },
        cancellationToken);

        if (!lockSuccessful)
        {
            throw new ConflictException();
        }

        return result!;
    }

    private async Task<FileMetadata> StoreImpl(
        DatasetVersion datasetVersion,
        FileType type,
        string filePath,
        string fullFilePath,
        Stream data,
        long size,
        string? contentType,
        CancellationToken cancellationToken)
    {
        /*async Task<string?> Deduplicate(byte[] checksum)
        {
            if (!TryGetPreviousVersion(datasetVersion.Version, out var prevVersion))
            {
                return null;
            }

            var prevDatasetVersion = new DatasetVersion(datasetVersion.Identifier, prevVersion);
            var prevManifest = await LoadManifest(prevDatasetVersion, true, CancellationToken.None);
            var itemsWithEqualChecksum = prevManifest.GetItemsByChecksum(checksum);

            if (!itemsWithEqualChecksum.Any())
            {
                return null;
            }

            var prevFetch = await LoadFetch(prevDatasetVersion, CancellationToken.None);

            // If we find an item with equal checksum in fetch.txt, use that URL
            foreach (var candidate in itemsWithEqualChecksum)
            {
                if (prevFetch.TryGetItem(candidate.FilePath, out var fetchItem))
                {
                    return fetchItem.Url;
                }
            }

            // Nothing found in fetch.txt, simply take first item's file path
            return "../" +
                UrlEncodePath(GetVersionPath(prevDatasetVersion)) + '/' +
                UrlEncodePath(itemsWithEqualChecksum.First().FilePath);
        }*/

        StorageFileBaseMetadata result;
        byte[] checksum;
        long bytesRead;

        using (var hashStream = new CountedHashStream(data))
        {
            result = await storageService.Store(
                fullFilePath,
                hashStream,
                size,
                contentType,
                cancellationToken);

            checksum = hashStream.GetHash();
            bytesRead = hashStream.BytesRead;
        }

        // Do not cancel the operation from this point on,
        // since the file has been successfully stored.

        /*string? url = await Deduplicate(checksum);
        if (url != null)
        {
            // Deduplication was successful, store in fetch and delete uploaded file
            await AddOrUpdateFetchItem(datasetVersion, new(filePath, bytesRead, url), CancellationToken.None);
            await storageService.DeleteFile(fullFilePath, CancellationToken.None);
        }
        else
        {
            // File is not a duplicate, remove from fetch if present there
            await RemoveItemFromFetch(datasetVersion, filePath, CancellationToken.None);
        }*/

        // Remove from fetch if present there
        await RemoveItemFromFetch(datasetVersion, filePath, CancellationToken.None);

        // Update payload manifest
        await AddOrUpdatePayloadManifestItem(datasetVersion, new(filePath, checksum), CancellationToken.None);

        return new(
            ContentType: result.ContentType ?? MimeTypes.GetMimeType(filePath),
            DateCreated: result.DateCreated,
            DateModified: result.DateModified,
            Path: filePath[Paths.GetPayloadPath(type).Length..],
            Sha256: checksum,
            Size: bytesRead,
            Type: type);
    }

    public async Task Delete(
        DatasetVersion datasetVersion,
        FileType type,
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        filePath = GetFilePathOrThrow(type, filePath);

        bool lockSuccessful = false;
        await lockService.TryLockDatasetVersionShared(datasetVersion, async () =>
        {
            string fullFilePath = Paths.GetFullFilePath(datasetVersion, filePath);

            lockSuccessful = await lockService.TryLockPath(fullFilePath, async () =>
            {
                await ThrowIfHasBeenPublished(datasetVersion, cancellationToken);
                await DeleteImpl(datasetVersion, filePath, fullFilePath, cancellationToken);
            },
            cancellationToken);
        },
        cancellationToken);

        if (!lockSuccessful)
        {
            throw new ConflictException();
        }
    }

    private async Task DeleteImpl(
        DatasetVersion datasetVersion,
        string filePath,
        string fullFilePath,
        CancellationToken cancellationToken)
    {
        await storageService.Delete(fullFilePath, cancellationToken);

        // Do not cancel the operation from this point on,
        // since the file has been successfully deleted.

        await RemoveItemFromPayloadManifest(datasetVersion, filePath, CancellationToken.None);
        await RemoveItemFromFetch(datasetVersion, filePath, CancellationToken.None);
    }

    public async Task Import(
       DatasetVersion datasetVersion,
       string fromVersion,
       CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(fromVersion);

        var fromDatasetVersion = new DatasetVersion(datasetVersion.Identifier, fromVersion);

        if (datasetVersion == fromDatasetVersion)
        {
            // Importing from and to the same version, do nothing.
            return;
        }

        if (!await metadataService.VersionHasBeenPublished(fromDatasetVersion, cancellationToken))
        {
            // fromVersion is not published, do nothing.
            return;
        }

        bool lockSuccessful = await lockService.TryLockDatasetVersionExclusive(datasetVersion, async () =>
        {
            await ThrowIfHasBeenPublished(datasetVersion, cancellationToken);
            await ImportImpl(
                datasetVersion,
                fromDatasetVersion,
                cancellationToken);
        },
        cancellationToken);

        if (!lockSuccessful)
        {
            throw new ConflictException();
        }
    }

    private async Task ImportImpl(
        DatasetVersion datasetVersion,
        DatasetVersion fromVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentNullException.ThrowIfNull(fromVersion);

        async Task<BagItFetch> PrepareFetch()
        {
            var fetch = await metadataService.LoadBagItElement<BagItFetch>(fromVersion, cancellationToken);
            string fromVersionUrl = "../" + UrlEncodePath(Paths.GetVersionPath(fromVersion)) + '/';

            await foreach (var file in metadataService.ListPayloadFiles(fromVersion, null, cancellationToken))
            {
                fetch.AddOrUpdateItem(new(file.Path, file.Size, fromVersionUrl + UrlEncodePath(file.Path)));
            }

            return fetch;
        }

        if (await metadataService.ListPayloadFiles(datasetVersion, null, cancellationToken)
            .GetAsyncEnumerator(cancellationToken).MoveNextAsync())
        {
            // Payload files present, do nothing.
            return;
        }

        var fetch = await PrepareFetch();
        var manifest = await metadataService.LoadBagItElement<BagItPayloadManifest>(fromVersion, cancellationToken);

        await metadataService.StoreBagItElement(datasetVersion, fetch, cancellationToken);
        await metadataService.StoreBagItElement(datasetVersion, manifest, CancellationToken.None);
    }

    public async Task<FileData?> GetData(
        DatasetVersion datasetVersion,
        FileType type,
        string filePath,
        bool isHeadRequest,
        ByteRange? byteRange,
        bool restrictToPubliclyAccessible,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        // Add some kind of locking here for read consistency?

        filePath = GetFilePathOrThrow(type, filePath);

        if (restrictToPubliclyAccessible)
        {
            // Do not return file data unless dataset version has been published, is not withdrawn,
            // and file type is either documentation (which entails publically accessible) or
            // access right is public.

            if (!await metadataService.VersionHasBeenPublished(datasetVersion, cancellationToken))
            {
                return null;
            }

            var bagInfo = await metadataService.LoadBagItElement<BagItInfo>(datasetVersion, cancellationToken);

            if (bagInfo == null)
            {
                return null;
            }

            if (bagInfo.GetDatasetVersionStatus() != DatasetVersionStatus.published ||
                type == FileType.data && bagInfo.GetAccessRight() != AccessRight.@public)
            {
                return null;
            }
        }

        var fetch = await metadataService.LoadBagItElement<BagItFetch>(datasetVersion, cancellationToken);
        filePath = GetActualFilePath(datasetVersion, fetch, filePath);

        FileData? result = null;
        if (isHeadRequest)
        {
            var metadata = await storageService.GetMetadata(filePath, cancellationToken);

            if (metadata != null)
            {
                result = new(
                    ContentType: metadata.ContentType,
                    Size: metadata.Size,
                    Stream: Stream.Null,
                    StreamLength: 0);
            }
        }
        else
        {
            var data = await storageService.GetData(
                filePath,
                byteRange == null ? null : new(byteRange.From, byteRange.To),
                cancellationToken);

            if (data != null)
            {
                result = new(
                    ContentType: data.ContentType,
                    Size: data.Size,
                    Stream: data.Stream,
                    StreamLength: data.StreamLength);
            }
        }

        if (result != null && result.ContentType == null)
        {
            result = result with { ContentType = MimeTypes.GetMimeType(filePath) };
        }

        return result;
    }

    public async IAsyncEnumerable<FileMetadata> List(
        DatasetVersion datasetVersion,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);

        // Add some kind of locking here?
        // Checksums and fetch can potentially be changed while processing this request,
        // leading to returning faulty checksums and other problems.

        var payloadManifest = await metadataService.LoadBagItElement<BagItPayloadManifest>(datasetVersion, cancellationToken);
        var fetch = await metadataService.LoadBagItElement<BagItFetch>(datasetVersion, cancellationToken);

        byte[]? GetChecksum(string filePath) =>
            payloadManifest.TryGetItem(filePath, out var value) ? value.Checksum : null;

        string datasetPath = Paths.GetDatasetPath(datasetVersion);
        var result = new List<StorageFileMetadata>();

        string previousPayloadPath = "";
        Dictionary<string, StorageFileMetadata> dict = new(StringComparer.Ordinal);
        foreach (var item in fetch.Items.OrderBy(i => i.Url, StringComparer.Ordinal))
        {
            string path = datasetPath + DecodeUrlEncodedPath(item.Url[3..]);
            string payloadPath = path[..(path.IndexOf("/data/", StringComparison.Ordinal) + 6)];

            if (payloadPath != previousPayloadPath)
            {
                dict = [];
                await foreach (var file in storageService.List(payloadPath, cancellationToken))
                {
                    dict[file.Path] = file;
                }
            }

            result.Add(dict[path] with { Path = item.FilePath });

            previousPayloadPath = payloadPath;
        }

        await foreach (var file in metadataService.ListPayloadFiles(datasetVersion, null, cancellationToken))
        {
            result.Add(file);
        }

        foreach (var file in result.OrderBy(f => f.Path, StringComparer.InvariantCulture))
        {
            var type = GetFileType(file.Path);

            yield return new(
                ContentType: file.ContentType ?? MimeTypes.GetMimeType(file.Path),
                DateCreated: file.DateCreated,
                DateModified: file.DateModified,
                Path: file.Path[Paths.GetPayloadPath(type).Length..],
                Sha256: GetChecksum(file.Path),
                Size: file.Size,
                Type: type);
        }
    }

    public async Task WriteDataAsZip(
        DatasetVersion datasetVersion,
        string[] paths,
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(stream);

        static Stream CreateZipEntryStream(ZipArchive zipArchive, string filePath)
        {
            var entry = zipArchive.CreateEntry(filePath, CompressionLevel.NoCompression);
            return entry.Open();
        }

        var payloadManifest = await metadataService.LoadBagItElement<BagItPayloadManifest>(datasetVersion, cancellationToken);
        var fetch = await metadataService.LoadBagItElement<BagItFetch>(datasetVersion, cancellationToken);
        string versionPath = Paths.GetVersionPath(datasetVersion);

        using var zipArchive = new ZipArchive(stream, ZipArchiveMode.Create, false);
        var sent = new List<BagItManifestItem>();

        foreach (var manifestItem in payloadManifest.Items)
        {
            string zipFilePath = manifestItem.FilePath[5..]; // Strip "data/"

            if (paths.Length > 0 &&
                !paths.Any(p => zipFilePath.StartsWith(p, StringComparison.Ordinal)))
            {
                continue;
            }

            string actualFilePath = GetActualFilePath(datasetVersion, fetch, manifestItem.FilePath);
            var fileData = await storageService.GetData(actualFilePath, null, cancellationToken);

            if (fileData != null)
            {
                using var entryStream = CreateZipEntryStream(zipArchive, versionPath + '/' + zipFilePath);
                using (fileData.Stream)
                {
                    await fileData.Stream.CopyToAsync(entryStream, cancellationToken);
                }

                sent.Add(new(zipFilePath, manifestItem.Checksum));
            }
        }

        if (sent.Count > 0)
        {
            // Send sha256.txt in the format used by UNIX utility sha256sum:
            // one line per file separated by newline,
            // each line composed of checksum + ' ' + file name.
            // File names with backslash or newline are escaped by
            // starting the line with a backslash and escaping \ as \\
            // and newline as \n.

            using var entryStream = CreateZipEntryStream(zipArchive, versionPath + "/sha256.txt");

            foreach (var file in sent)
            {
                string name = file.FilePath
                    .Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\n", "\\n", StringComparison.Ordinal);

                await entryStream.WriteAsync(Encoding.UTF8.GetBytes(
                    (name.Length > file.FilePath.Length
                        ? "\\"
                        : "") +
#pragma warning disable CA1308 // Normalize strings to uppercase
                    Convert.ToHexString(file.Checksum).ToLowerInvariant() + ' ' + name + '\n'),
#pragma warning disable CA1308
                    cancellationToken);
            }
        }
    }


    private static string GetFilePathOrThrow(FileType type, string filePath)
    {
        foreach (string pathComponent in filePath.Split('/'))
        {
            if (string.IsNullOrEmpty(pathComponent) ||
                pathComponent == "." ||
                pathComponent == "..")
            {
                throw new IllegalPathException();
            }
        }

        return Paths.GetPayloadPath(type) + filePath;
    }

    private static string UrlEncodePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static string DecodeUrlEncodedPath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.UnescapeDataString));

    private static FileType GetFileType(string path)
    {
        if (path.StartsWith(Paths.GetPayloadPath(FileType.data), StringComparison.Ordinal))
        {
            return FileType.data;
        }

        if (path.StartsWith(Paths.GetPayloadPath(FileType.documentation), StringComparison.Ordinal))
        {
            return FileType.documentation;
        }

        throw new ArgumentException("Not a valid payload path.", nameof(path));
    }

    private static string GetActualFilePath(DatasetVersion datasetVersion, BagItFetch fetch, string filePath)
    {
        if (fetch.TryGetItem(filePath, out var fetchItem))
        {
            return Paths.GetDatasetPath(datasetVersion) + DecodeUrlEncodedPath(fetchItem.Url[3..]);
        }

        return Paths.GetFullFilePath(datasetVersion, filePath);
    }

    private async Task ThrowIfHasBeenPublished(DatasetVersion datasetVersion, CancellationToken cancellationToken)
    {
        if (await metadataService.VersionHasBeenPublished(datasetVersion, cancellationToken))
        {
            throw new DatasetStatusException();
        }
    }

    private Task AddOrUpdatePayloadManifestItem(
        DatasetVersion datasetVersion,
        BagItManifestItem item,
        CancellationToken cancellationToken) =>
        LockAndUpdateBagItElement<BagItPayloadManifest>(
            datasetVersion, manifest => manifest.AddOrUpdateItem(item), cancellationToken);

    private Task RemoveItemFromPayloadManifest(
        DatasetVersion datasetVersion,
        string filePath,
        CancellationToken cancellationToken) =>
        LockAndUpdateBagItElement<BagItPayloadManifest>(
            datasetVersion, manifest => manifest.RemoveItem(filePath), cancellationToken);

    private Task RemoveItemFromFetch(
        DatasetVersion datasetVersion,
        string filePath,
        CancellationToken cancellationToken) =>
        LockAndUpdateBagItElement<BagItFetch>(datasetVersion, fetch => fetch.RemoveItem(filePath), cancellationToken);

    private async Task LockAndUpdateBagItElement<T>(
        DatasetVersion datasetVersion,
        Func<T, bool> action,
        CancellationToken cancellationToken)
        where T : class, IBagItElement<T>, new()
    {
        // This method assumes that there is no exlusive lock on datasetVersion

        using (await lockService.LockPath(Paths.GetFullFilePath(datasetVersion, T.FileName), cancellationToken))
        {
            var element = await metadataService.LoadBagItElement<T>(datasetVersion, cancellationToken);

            if (action(element))
            {
                await metadataService.StoreBagItElement(datasetVersion, element, cancellationToken);
            }
        }
    }
}
