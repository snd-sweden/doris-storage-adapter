using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.BagIt;
using DorisStorageAdapter.Services.Implementation.BagIt.Fetch;
using DorisStorageAdapter.Services.Implementation.BagIt.Info;
using DorisStorageAdapter.Services.Implementation.BagIt.Manifest;
using DorisStorageAdapter.Services.Implementation.Configuration;
using DorisStorageAdapter.Services.Implementation.Lock;
using DorisStorageAdapter.Services.Implementation.Storage;
using Microsoft.Extensions.Options;
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
    IBagProvider bagProvider,
    IOptions<StorageConfiguration> storageConfiguration) : IFileService
{
    private readonly IStorageService _storageService = storageService;
    private readonly ILockService _lockService = lockService;
    private readonly IBagProvider _bagProvider = bagProvider;
    private readonly StorageConfiguration _storageConfiguration = storageConfiguration.Value;

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
        Validation.ThrowIfInvalidDatasetVersion(datasetVersion);

        filePath = GetFilePathOrThrow(type, filePath);
        FileMetadata? result = default;

        bool lockSuccessful = false;
        await _lockService.TryLockDatasetVersionShared(datasetVersion, async () =>
        {
            var bag = _bagProvider.Create(datasetVersion);
            string fullFilePath = bag.Path + filePath;

            lockSuccessful = await _lockService.TryLockPath(fullFilePath, async () =>
            {          
                await ThrowIfHasBeenPublished(bag, cancellationToken);

                result = await StoreImpl(
                    bag,
                    type,
                    filePath,
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
        Bag bag,
        FileType type,
        string filePath,
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
            result = await _storageService.Store(
                bag.Path + filePath,
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
        await RemoveItemFromFetch(bag, filePath, CancellationToken.None);

        // Update payload manifest
        await AddOrUpdatePayloadManifestItem(bag, new(filePath, checksum), CancellationToken.None);

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
        Validation.ThrowIfInvalidDatasetVersion(datasetVersion);

        filePath = GetFilePathOrThrow(type, filePath);

        bool lockSuccessful = false;
        await _lockService.TryLockDatasetVersionShared(datasetVersion, async () =>
        {
            var bag = _bagProvider.Create(datasetVersion);
            string fullFilePath = bag.Path + filePath;

            lockSuccessful = await _lockService.TryLockPath(fullFilePath, async () =>
            {
                await ThrowIfHasBeenPublished(bag, cancellationToken);
                await DeleteImpl(bag, filePath, cancellationToken);
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
        Bag bag,
        string filePath,
        CancellationToken cancellationToken)
    {
        await _storageService.Delete(bag.Path + filePath, cancellationToken);

        // Do not cancel the operation from this point on,
        // since the file has been successfully deleted.

        await RemoveItemFromPayloadManifest(bag, filePath, CancellationToken.None);
        await RemoveItemFromFetch(bag, filePath, CancellationToken.None);
    }

    public async Task Import(
       DatasetVersion datasetVersion,
       string fromVersion,
       CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(fromVersion);
        Validation.ThrowIfInvalidDatasetVersion(datasetVersion);

        var fromDatasetVersion = new DatasetVersion(datasetVersion.Identifier, fromVersion);
        Validation.ThrowIfInvalidDatasetVersion(fromDatasetVersion);

        if (datasetVersion == fromDatasetVersion)
        {
            // Importing from and to the same version, do nothing.
            return;
        }

        var bag = _bagProvider.Create(datasetVersion);
        var fromBag = _bagProvider.Create(fromDatasetVersion);
     
        if (!await fromBag.HasBeenPublished(cancellationToken))
        {
            // fromVersion is not published, do nothing.
            return;
        }

        bool lockSuccessful = await _lockService.TryLockDatasetVersionExclusive(datasetVersion, async () =>
        {
            await ThrowIfHasBeenPublished(bag, cancellationToken);
            await ImportImpl(
                bag,
                fromBag,
                cancellationToken);
        },
        cancellationToken);

        if (!lockSuccessful)
        {
            throw new ConflictException();
        }
    }

    private static async Task ImportImpl(
        Bag bag,
        Bag fromBag,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bag);
        ArgumentNullException.ThrowIfNull(fromBag);

        async Task<BagItFetch> PrepareFetch()
        {
            var fetch = await fromBag.LoadBagItElement<BagItFetch>(cancellationToken);
            // TODO refactor handle fetch URL
            string fromVersionUrl = "../" + UrlEncodePath(fromBag.Path.TrimEnd('/').Split('/').Last()) + '/';

            await foreach (var file in fromBag.ListPayloadFiles(cancellationToken))
            {
                fetch.AddOrUpdateItem(new(file.Path, file.Size, fromVersionUrl + UrlEncodePath(file.Path)));
            }

            return fetch;
        }

        if (await bag.ListPayloadFiles(cancellationToken)
            .GetAsyncEnumerator(cancellationToken).MoveNextAsync())
        {
            // Payload files present, do nothing.
            return;
        }

        var fetch = await PrepareFetch();
        var manifest = await fromBag.LoadBagItElement<BagItPayloadManifest>(cancellationToken);

        await bag.StoreBagItElement(fetch, cancellationToken);
        await bag.StoreBagItElement(manifest, CancellationToken.None);
    }

    public async Task<FileData?> GetData(
        DatasetVersion datasetVersion,
        FileType type,
        string filePath,
        bool isHeadRequest,
        ByteRange? byteRange,
        bool allowDraft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        Validation.ThrowIfInvalidDatasetVersion(datasetVersion);

        // Add some kind of locking here for read consistency?

        filePath = GetFilePathOrThrow(type, filePath);
        var bag = _bagProvider.Create(datasetVersion);

        var accessibleFileTypes = await GetAccessibleFileTypes(
            bag, allowDraft, cancellationToken);

        if (!accessibleFileTypes.Contains(type))
        {
            return null;
        }

        var fetch = await bag.LoadBagItElement<BagItFetch>(cancellationToken);
        filePath = GetActualFilePath(bag, fetch, filePath);

        FileData? result = null;
        if (isHeadRequest)
        {
            var metadata = await _storageService.GetMetadata(filePath, cancellationToken);

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
            var data = await _storageService.GetData(
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
        Validation.ThrowIfInvalidDatasetVersion(datasetVersion);

        // Add some kind of locking here?
        // Checksums and fetch can potentially be changed while processing this request,
        // leading to returning faulty checksums and other problems.

        var bag = _bagProvider.Create(datasetVersion);

        var payloadManifest = await bag.LoadBagItElement<BagItPayloadManifest>(cancellationToken);
        var fetch = await bag.LoadBagItElement<BagItFetch>(cancellationToken);

        byte[]? GetChecksum(string filePath) =>
            payloadManifest.TryGetItem(filePath, out var value) ? value.Checksum : null;

        var result = new List<StorageFileMetadata>();

        string previousPath = "";
        Dictionary<string, StorageFileMetadata> dict = new(StringComparer.Ordinal);
        foreach (var item in fetch.Items.OrderBy(i => i.Url, StringComparer.Ordinal))
        {
            (string versionPath, string filePath) = Paths.ParseFetchUrl(item.Url);
            var referencedBag = _bagProvider.Create(bag.BagGroupPath + versionPath);

            if (referencedBag.Path != previousPath)
            {
                dict = [];
                await foreach (var file in bag.ListPayloadFiles(cancellationToken))
                {
                    dict[file.Path] = file;
                }
            }

            result.Add(dict[filePath] with { Path = item.FilePath });

            previousPath = referencedBag.Path;
        }

        await foreach (var file in bag.ListPayloadFiles(cancellationToken))
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
        bool allowDraft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(stream);
        Validation.ThrowIfInvalidDatasetVersion(datasetVersion);

        var bag = _bagProvider.Create(datasetVersion);
 
        var accessibleFileTypes = await GetAccessibleFileTypes(
           bag, allowDraft, cancellationToken);

        static Stream CreateZipEntryStream(ZipArchive zipArchive, string filePath)
        {
            var entry = zipArchive.CreateEntry(filePath, CompressionLevel.NoCompression);
            return entry.Open();
        }

        var payloadManifest = await bag.LoadBagItElement<BagItPayloadManifest>(cancellationToken);
        var fetch = await bag.LoadBagItElement<BagItFetch>(cancellationToken);
        string versionPath = datasetVersion.Identifier + '-' + datasetVersion.Version;

        using var zipArchive = new ZipArchive(stream, ZipArchiveMode.Create, false);
        var sent = new List<BagItManifestItem>();

        foreach (var manifestItem in payloadManifest.Items)
        {
            if (!accessibleFileTypes.Contains(GetFileType(manifestItem.FilePath)))
            {
                continue;
            }

            string zipFilePath = manifestItem.FilePath[5..]; // Strip "data/"

            if (paths.Length > 0 &&
                !paths.Any(p => zipFilePath.StartsWith(p, StringComparison.Ordinal)))
            {
                continue;
            }

            string actualFilePath = GetActualFilePath(bag, fetch, manifestItem.FilePath);
            var fileData = await _storageService.GetData(actualFilePath, null, cancellationToken);

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
                throw new ValidationException([new("Invalid path.")]);
            }
        }

        return Paths.GetPayloadPath(type) + filePath;
    }

    private static string UrlEncodePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

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

    // TODO refactor
    private static string GetActualFilePath(Bag bag, BagItFetch fetch, string filePath)
    {
        if (fetch.TryGetItem(filePath, out var fetchItem))
        {
            // Eftersom jag eg. vill ha hela här så blir det lite konstigt
            (string fetchVersionPath, string fetchFilePath) = Paths.ParseFetchUrl(fetchItem.Url);

            return bag.Path + fetchVersionPath + fetchFilePath;
        }

        return bag.Path + filePath;
    }

    private static async Task ThrowIfHasBeenPublished(Bag bag, CancellationToken cancellationToken)
    {
        if (await bag.HasBeenPublished(cancellationToken))
        {
            throw new DatasetStatusException();
        }
    }

    private async Task<IEnumerable<FileType>> GetAccessibleFileTypes(
        Bag bag, bool allowDraft, CancellationToken cancellationToken)
    {
        if (await bag.HasBeenPublished(cancellationToken))
        {
            var bagInfo = await bag.LoadBagItElement<BagItInfo>(cancellationToken);

            if (bagInfo == null)
            {
                return [];
            }

            // Check that version is not withdrawn.
            if (bagInfo.GetDatasetVersionStatus() != DatasetVersionStatus.published)
            {
                return [];
            }

            // If we reach here, we know we can allow access to documentation files.
            // Check if we also allow access to data files.
            if (_storageConfiguration.AllowPublicAccessRight &&
                bagInfo.GetAccessRight() == AccessRight.@public)
            {
                // Yes, we allow public data files and access right
                // is set to public.
                return [FileType.data, FileType.documentation];
            }

            // No, only allow access to documentation files.
            return [FileType.documentation];
        }
        else if (allowDraft)
        {
            // Dataset version has not been published and allowDraft is true,
            // allow access to all files.
            return [FileType.data, FileType.documentation];
        }

        return [];
    }

    private Task AddOrUpdatePayloadManifestItem(
        Bag bag,
        BagItManifestItem item,
        CancellationToken cancellationToken) =>
        LockAndUpdateBagItElement<BagItPayloadManifest>(
            bag, manifest => manifest.AddOrUpdateItem(item), cancellationToken);

    private Task RemoveItemFromPayloadManifest(
        Bag bag,
        string filePath,
        CancellationToken cancellationToken) =>
        LockAndUpdateBagItElement<BagItPayloadManifest>(
            bag, manifest => manifest.RemoveItem(filePath), cancellationToken);

    private Task RemoveItemFromFetch(
        Bag bag,
        string filePath,
        CancellationToken cancellationToken) =>
        LockAndUpdateBagItElement<BagItFetch>(bag, fetch => fetch.RemoveItem(filePath), cancellationToken);

    private async Task LockAndUpdateBagItElement<T>(
        Bag bag,
        Func<T, bool> action,
        CancellationToken cancellationToken)
        where T : class, IBagItElement<T>, new()
    {
        // This method assumes that there is no exlusive lock on datasetVersion

        using (await _lockService.LockPath(bag.Path + T.FileName, cancellationToken))
        {
            var element = await bag.LoadBagItElement<T>(cancellationToken);

            if (action(element))
            {
                await bag.StoreBagItElement(element, cancellationToken);
            }
        }
    }
}
