﻿using ByteSizeLib;
using DorisStorageAdapter.Configuration;
using DorisStorageAdapter.Models;
using DorisStorageAdapter.Services.BagIt;
using DorisStorageAdapter.Services.Exceptions;
using DorisStorageAdapter.Services.Lock;
using DorisStorageAdapter.Services.Storage;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services;

public class ServiceImplementation(
    IStorageService storageService,
    ILockService lockService,
    IOptions<GeneralConfiguration> generalConfiguration)
{
    private readonly IStorageService storageService = storageService;
    private readonly ILockService lockService = lockService;

    private readonly GeneralConfiguration generalConfiguration = generalConfiguration.Value;

    private const string payloadManifestSha256FileName = "manifest-sha256.txt";
    private const string tagManifestSha256FileName = "tagmanifest-sha256.txt";
    private const string fetchFileName = "fetch.txt";
    private const string bagItFileName = "bagit.txt";
    private const string bagInfoFileName = "bag-info.txt";

    public async Task PublishDatasetVersion(
        DatasetVersionIdentifier datasetVersion,
        AccessRight accessRight,
        string doi,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(doi);

        bool lockSuccessful = await lockService.TryLockDatasetVersionExclusive(datasetVersion, async () =>
        {
            await PublishDatasetVersionImpl(datasetVersion, accessRight, doi, cancellationToken);
        },
        cancellationToken);

        if (!lockSuccessful)
        {
            throw new ConflictException();
        }
    }

    private async Task PublishDatasetVersionImpl(
        DatasetVersionIdentifier datasetVersion,
        AccessRight accessRight,
        string doi,
        CancellationToken cancellationToken)
    {
        async Task<(T, byte[] Checksum)?> LoadWithChecksum<T>(string filePath, Func<Stream, CancellationToken, Task<T>> func)
        {
            var fileData = await storageService.GetFileData(filePath, cancellationToken);

            if (fileData == null)
            {
                return null;
            }

            using var hashStream = new CountedHashStream(fileData.Stream);

            return (await func(hashStream, cancellationToken), hashStream.GetHash());
        }

        Task<(BagItManifest Manifest, byte[] Checksum)?> LoadManifestWithChecksum() =>
            LoadWithChecksum(GetManifestFilePath(datasetVersion, true), BagItManifest.Parse);

        Task<(BagItFetch Fetch, byte[] Checksum)?> LoadFetchWithChecksum() =>
            LoadWithChecksum(GetFetchFilePath(datasetVersion), BagItFetch.Parse);

        var fetch = await LoadFetchWithChecksum();
        long octetCount = 0;
        bool payloadFileFound = false;
        await foreach (var file in ListPayloadFiles(datasetVersion, null, cancellationToken))
        {
            payloadFileFound = true;
            octetCount += file.Length;
        }
        foreach (var item in fetch?.Fetch?.Items ?? [])
        {
            payloadFileFound = true;
            if (item.Length != null)
            {
                octetCount += item.Length.Value;
            }
        }

        if (!payloadFileFound)
        {
            // No payload files found, abort
            return;
        }

        var payloadManifest = await LoadManifestWithChecksum();

        var bagInfo = new BagItInfo
        {
            BaggingDate = DateTime.UtcNow,
            BagGroupIdentifier = datasetVersion.DatasetIdentifier,
            BagSize = ByteSize.FromBytes(octetCount).ToBinaryString(CultureInfo.InvariantCulture),
            ExternalIdentifier = doi,
            PayloadOxum = new(octetCount, payloadManifest?.Manifest?.Items?.LongCount() ?? 0),
            AccessRight = accessRight,
            DatasetStatus = DatasetStatus.completed,
            Version = datasetVersion.VersionNumber
        };
        byte[] bagInfoContents = bagInfo.Serialize();

        byte[] bagItContents = Encoding.UTF8.GetBytes("BagIt-Version: 1.0\nTag-File-Character-Encoding: UTF-8");

        // Add bagit.txt, bag-info.txt and manifest-sha256.txt to tagmanifest-sha256.txt
        var tagManifest = await LoadManifest(datasetVersion, false, cancellationToken);
        tagManifest.AddOrUpdateItem(new(bagItFileName, SHA256.HashData(bagItContents)));
        tagManifest.AddOrUpdateItem(new(bagInfoFileName, SHA256.HashData(bagInfoContents)));
        if (payloadManifest != null)
        {
            tagManifest.AddOrUpdateItem(new(payloadManifestSha256FileName, payloadManifest.Value.Checksum));
        }
        if (fetch != null)
        {
            tagManifest.AddOrUpdateItem(new(fetchFileName, fetch.Value.Checksum));
        }

        await StoreManifest(datasetVersion, false, tagManifest, cancellationToken);
        await StoreBagInfo(datasetVersion, bagInfoContents, CancellationToken.None);
        await StoreFileAndDispose(
            GetBagItFilePath(datasetVersion),
            CreateFileDataFromByteArray(bagItContents),
            CancellationToken.None);
    }

    public async Task WithdrawDatasetVersion(
        DatasetVersionIdentifier datasetVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);

        bool lockSuccessful = await lockService.TryLockDatasetVersionExclusive(datasetVersion, async () =>
        {
            if (!await VersionHasBeenPublished(datasetVersion, cancellationToken))
            {
                throw new DatasetStatusException();
            }

            await WithdrawDatasetVersionImpl(datasetVersion, cancellationToken);
        },
        cancellationToken);

        if (!lockSuccessful)
        {
            throw new ConflictException();
        }
    }

    private async Task WithdrawDatasetVersionImpl(
        DatasetVersionIdentifier datasetVersion,
        CancellationToken cancellationToken)
    {
        var bagInfo = await LoadBagInfo(datasetVersion, cancellationToken);

        if (bagInfo == null)
        {
            // Do we need to throw an exception here?
            return;
        }

        bagInfo.DatasetStatus = DatasetStatus.withdrawn;

        var bagInfoContents = bagInfo.Serialize();

        var tagManifest = await LoadManifest(datasetVersion, false, cancellationToken);
        tagManifest.AddOrUpdateItem(new(bagInfoFileName, SHA256.HashData(bagInfoContents)));
        await StoreManifest(datasetVersion, false, tagManifest, cancellationToken);

        await StoreBagInfo(datasetVersion, bagInfoContents, CancellationToken.None);
    }

    public async Task<Models.File> StoreFile(
        DatasetVersionIdentifier datasetVersion,
        FileType type,
        string filePath,
        FileData data,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(data);

        filePath = GetFilePathOrThrow(type, filePath);
        Models.File? result = default;

        bool lockSuccessful = false;
        await lockService.TryLockDatasetVersionShared(datasetVersion, async () =>
        {
            string fullFilePath = GetFullFilePath(datasetVersion, filePath);

            lockSuccessful = await lockService.TryLockPath(fullFilePath, async () =>
            {
                await ThrowIfHasBeenPublished(datasetVersion, cancellationToken);
                result = await StoreFileImpl(
                    datasetVersion,
                    filePath,
                    fullFilePath,
                    data,
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

    private async Task<Models.File> StoreFileImpl(
        DatasetVersionIdentifier datasetVersion,
        string filePath,
        string fullFilePath,
        FileData data,
        CancellationToken cancellationToken)
    {
        /*async Task<string?> Deduplicate(byte[] checksum)
        {
            if (!TryGetPreviousVersionNumber(datasetVersion.VersionNumber, out var prevVersionNr))
            {
                return null;
            }

            var prevVersion = new DatasetVersionIdentifier(datasetVersion.DatasetIdentifier, prevVersionNr);
            var prevManifest = await LoadManifest(prevVersion, true, CancellationToken.None);
            var itemsWithEqualChecksum = prevManifest.GetItemsByChecksum(checksum);

            if (!itemsWithEqualChecksum.Any())
            {
                return null;
            }

            var prevFetch = await LoadFetch(prevVersion, CancellationToken.None);

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
                UrlEncodePath(GetVersionPath(prevVersion)) + '/' +
                UrlEncodePath(itemsWithEqualChecksum.First().FilePath);
        }*/

        StorageServiceFileBase result;
        byte[] checksum;
        long bytesRead;

        using (var hashStream = new CountedHashStream(data.Stream))
        {
            result = await storageService.StoreFile(
                fullFilePath,
                data with { Stream = hashStream },
                cancellationToken);

            checksum = hashStream.GetHash();
            bytesRead = hashStream.BytesRead;
        }

        // From this point on we do not want to cancel the operation,
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

        // Remove from fetch if present there.
        await RemoveItemFromFetch(datasetVersion, filePath, CancellationToken.None);

        // Update payload manifest.
        await AddOrUpdatePayloadManifestItem(datasetVersion, new(filePath, checksum), CancellationToken.None);

        return ToModelFile(datasetVersion, new(
            ContentType: result.ContentType,
            DateCreated: result.DateCreated,
            DateModified: result.DateModified,
            Path: filePath,
            Length: bytesRead),
        checksum);
    }

    public async Task DeleteFile(
        DatasetVersionIdentifier datasetVersion,
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
            string fullFilePath = GetFullFilePath(datasetVersion, filePath);

            lockSuccessful = await lockService.TryLockPath(fullFilePath, async () =>
            {
                await ThrowIfHasBeenPublished(datasetVersion, cancellationToken);
                await DeleteFileImpl(datasetVersion, filePath, fullFilePath, cancellationToken);
            },
            cancellationToken);
        },
        cancellationToken);

        if (!lockSuccessful)
        {
            throw new ConflictException();
        }
    }

    private async Task DeleteFileImpl(
        DatasetVersionIdentifier datasetVersion,
        string filePath,
        string fullFilePath,
        CancellationToken cancellationToken)
    {
        await storageService.DeleteFile(fullFilePath, cancellationToken);

        // From this point on we do not want to cancel the operation,
        // since the file has been successfully deleted.

        await RemoveItemFromPayloadManifest(datasetVersion, filePath, CancellationToken.None);
        await RemoveItemFromFetch(datasetVersion, filePath, CancellationToken.None);
    }

    public async Task ImportFiles(
       DatasetVersionIdentifier datasetVersion,
       FileType type,
       string fromVersionNumber,
       CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(fromVersionNumber);

        bool lockSuccessful = await lockService.TryLockDatasetVersionExclusive(datasetVersion, async () =>
        {
            await ThrowIfHasBeenPublished(datasetVersion, cancellationToken);
            await ImportFilesImpl(
                datasetVersion, 
                type, 
                new(datasetVersion.DatasetIdentifier, fromVersionNumber), 
                cancellationToken);
        },
        cancellationToken);

        if (!lockSuccessful)
        {
            throw new ConflictException();
        }
    }

    private async Task ImportFilesImpl(
        DatasetVersionIdentifier datasetVersion,
        FileType type,
        DatasetVersionIdentifier fromVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentNullException.ThrowIfNull(fromVersion);

        async Task<BagItFetch> PrepareFetch()
        {
            var fromFetch = await LoadFetch(fromVersion, cancellationToken);
            var fetch = await LoadFetch(datasetVersion, cancellationToken);

            foreach (var item in fromFetch.Items)
            {
                if (GetFileType(item.FilePath) == type)
                {
                    fetch.AddOrUpdateItem(item);
                }
            }

            string fromVersionUrl = "../" + UrlEncodePath(GetVersionPath(fromVersion)) + '/';

            await foreach (var file in ListPayloadFiles(fromVersion, type, cancellationToken))
            {
                if (!fromFetch.Contains(file.Path))
                {
                    fetch.AddOrUpdateItem(new(file.Path, file.Length, fromVersionUrl + UrlEncodePath(file.Path)));
                }
            }

            return fetch;
        }

        async Task<BagItManifest> PreparePayloadManifest()
        {
            var fromManifest = await LoadManifest(fromVersion, true, cancellationToken);
            var manifest = await LoadManifest(datasetVersion, true, cancellationToken);

            foreach (var item in fromManifest.Items)
            {
                if (GetFileType(item.FilePath) == type)
                {
                    manifest.AddOrUpdateItem(item);
                }
            }

            return manifest;
        }

        if (await ListPayloadFiles(datasetVersion, type, cancellationToken)
            .GetAsyncEnumerator(cancellationToken).MoveNextAsync())
        {
            // Payload files present for the given file type, abort.
            return;
        }

        var fetch = await PrepareFetch();
        var manifest = await PreparePayloadManifest();

        await StoreFetch(datasetVersion, fetch, cancellationToken);
        await StoreManifest(datasetVersion, true, manifest, CancellationToken.None);
    }

    public async Task<FileData?> GetFileData(
        DatasetVersionIdentifier datasetVersion,
        FileType type,
        string filePath,
        bool restrictToPubliclyAccessible,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        // Should we add some kind of locking here?
        // The requested file could potentially be added to fetch and removed from current version
        // after we found it in fetch and try to load it from current version, which will return
        // not found to the caller.

        filePath = GetFilePathOrThrow(type, filePath);

        if (restrictToPubliclyAccessible)
        {
            // Do not return file data unless dataset version is published (and not withdrawn),
            // and file type is either documentation (which entails publically accessible) or
            // access right is public.

            var bagInfo = await LoadBagInfo(datasetVersion, cancellationToken);

            if (bagInfo == null)
            {
                return null;
            }

            if (bagInfo.DatasetStatus != DatasetStatus.completed ||
                type == FileType.data && bagInfo.AccessRight != AccessRight.@public)
            {
                return null;
            }
        }

        var fetch = await LoadFetch(datasetVersion, cancellationToken);
        var result = await storageService.GetFileData(
            GetActualFilePath(datasetVersion, fetch, filePath),
            cancellationToken);

        if (result != null && result.ContentType == null)
        {
            result = result with { ContentType = MimeTypes.GetMimeType(filePath) };
        }

        return result;
    }

    public async IAsyncEnumerable<Models.File> ListFiles(
        DatasetVersionIdentifier datasetVersion,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);

        // Should we add some kind of locking here?
        // Checksums and fetch can potentially be changed while processing this request,
        // leading to returning faulty checksums and other problems.

        var payloadManifest = await LoadManifest(datasetVersion, true, cancellationToken);
        var fetch = await LoadFetch(datasetVersion, cancellationToken);

        byte[]? GetChecksum(string filePath) =>
            payloadManifest.TryGetItem(filePath, out var value) ? value.Checksum : null;

        string datasetPath = GetDatasetPath(datasetVersion);
        var result = new List<StorageServiceFile>();

        string previousPayloadPath = "";
        Dictionary<string, StorageServiceFile> dict = new(StringComparer.Ordinal);
        foreach (var item in fetch.Items.OrderBy(i => i.Url, StringComparer.Ordinal))
        {
            string path = datasetPath + DecodeUrlEncodedPath(item.Url[3..]);
            string payloadPath = path[..(path.IndexOf("/data/", StringComparison.Ordinal) + 6)];

            if (payloadPath != previousPayloadPath)
            {
                dict = [];
                await foreach (var file in storageService.ListFiles(payloadPath, cancellationToken))
                {
                    dict[file.Path] = file;
                }
            }

            result.Add(dict[path] with { Path = item.FilePath });

            previousPayloadPath = payloadPath;
        }

        await foreach (var file in ListPayloadFiles(datasetVersion, null, cancellationToken))
        {
            result.Add(file);
        }

        foreach (var file in result.OrderBy(f => f.Path, StringComparer.InvariantCulture))
        {
            yield return ToModelFile(datasetVersion, file, GetChecksum(file.Path));
        }
    }

    public async Task WriteFileDataAsZip(
        DatasetVersionIdentifier datasetVersion,
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

        var payloadManifest = await LoadManifest(datasetVersion, true, cancellationToken);
        var fetch = await LoadFetch(datasetVersion, cancellationToken);
        string versionPath = GetVersionPath(datasetVersion);

        using var zipArchive = new ZipArchive(stream, ZipArchiveMode.Create, false);
        var zipManifest = new BagItManifest();

        foreach (var manifestItem in payloadManifest.Items)
        {
            string zipFilePath = manifestItem.FilePath[5..]; // Strip "data/"

            if (paths.Length > 0 &&
                !paths.Any(p => zipFilePath.StartsWith(p, StringComparison.Ordinal)))
            {
                continue;
            }

            string actualFilePath = GetActualFilePath(datasetVersion, fetch, manifestItem.FilePath);
            var data = await storageService.GetFileData(actualFilePath, cancellationToken);

            if (data != null)
            {
                using var entryStream = CreateZipEntryStream(zipArchive, versionPath + '/' + zipFilePath);
                using (data.Stream)
                {
                    await data.Stream.CopyToAsync(entryStream, cancellationToken);
                }

                zipManifest.AddOrUpdateItem(new(zipFilePath, manifestItem.Checksum));
            }
        }

        if (zipManifest.Items.Any())
        {
            using var entryStream = CreateZipEntryStream(
                zipArchive, versionPath + '/' + payloadManifestSha256FileName);
            var content = zipManifest.Serialize();
            await entryStream.WriteAsync(content, cancellationToken);
        }
    }

    private static string GetPayloadPath(FileType? type) =>
        "data/" + (type == null ? "" : type.ToString() + '/');

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

        return GetPayloadPath(type) + filePath;
    }

    private static string GetDatasetPath(DatasetVersionIdentifier datasetVersion) =>
        datasetVersion.DatasetIdentifier + '/';

    private static string GetVersionPath(DatasetVersionIdentifier datasetVersion) =>
        datasetVersion.DatasetIdentifier + '-' + datasetVersion.VersionNumber;

    private static string GetDatasetVersionPath(DatasetVersionIdentifier datasetVersion) =>
        GetDatasetPath(datasetVersion) + GetVersionPath(datasetVersion) + '/';

    private static string GetFullFilePath(DatasetVersionIdentifier datasetVersion, string filePath) =>
        GetDatasetVersionPath(datasetVersion) + filePath;

    private static string GetManifestFileName(bool payload) =>
        payload ? payloadManifestSha256FileName : tagManifestSha256FileName;

    private static string GetManifestFilePath(DatasetVersionIdentifier datasetVersion, bool payload) =>
        GetFullFilePath(datasetVersion, GetManifestFileName(payload));

    private static string GetFetchFilePath(DatasetVersionIdentifier datasetVersion) =>
        GetFullFilePath(datasetVersion, fetchFileName);

    private static string GetBagInfoFilePath(DatasetVersionIdentifier datasetVersion) =>
        GetFullFilePath(datasetVersion, bagInfoFileName);

    private static string GetBagItFilePath(DatasetVersionIdentifier datasetVersion) =>
        GetFullFilePath(datasetVersion, bagItFileName);

    private static string UrlEncodePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static string DecodeUrlEncodedPath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.UnescapeDataString));

    private static FileType GetFileType(string path)
    {
        if (path.StartsWith(GetPayloadPath(FileType.data), StringComparison.Ordinal))
        {
            return FileType.data;
        }

        if (path.StartsWith(GetPayloadPath(FileType.documentation), StringComparison.Ordinal))
        {
            return FileType.documentation;
        }

        throw new ArgumentException("Not a valid payload path.", nameof(path)); 
    }

    private Models.File ToModelFile(DatasetVersionIdentifier datasetVersion, StorageServiceFile file, byte[]? sha256)
    {
        var type = GetFileType(file.Path);
        string name = file.Path[GetPayloadPath(type).Length..];

        return new()
        {
            Name = name,
            Type = type,
            ContentSize = file.Length,
            DateCreated = file.DateCreated,
            DateModified = file.DateModified,
            EncodingFormat = file.ContentType ?? MimeTypes.GetMimeType(file.Path),
            Sha256 = sha256 == null ? null : Convert.ToHexString(sha256),
            Url = new Uri(generalConfiguration.PublicUrl, "file/" +
                UrlEncodePath(datasetVersion.DatasetIdentifier + '/' + datasetVersion.VersionNumber + '/' + type) +
                "?filePath=" + Uri.EscapeDataString(name))
        };
    }

    private async Task<BagItManifest> LoadManifest(
        DatasetVersionIdentifier datasetVersion,
        bool payloadManifest,
        CancellationToken cancellationToken)
    {
        var data = await storageService.GetFileData(
            GetManifestFilePath(datasetVersion, payloadManifest),
            cancellationToken);

        if (data == null)
        {
            return new();
        }

        using (data.Stream)
        {
            return await BagItManifest.Parse(data.Stream, cancellationToken);
        }
    }

    private async Task<BagItFetch> LoadFetch(
        DatasetVersionIdentifier datasetVersion,
        CancellationToken cancellationToken)
    {
        var data = await storageService.GetFileData(
            GetFetchFilePath(datasetVersion),
            cancellationToken);

        if (data == null)
        {
            return new();
        }

        using (data.Stream)
        {
            return await BagItFetch.Parse(data.Stream, cancellationToken);
        }
    }

    private async Task<BagItInfo?> LoadBagInfo(DatasetVersionIdentifier datasetVersion, CancellationToken cancellationToken)
    {
        var data = await storageService.GetFileData(
            GetBagInfoFilePath(datasetVersion),
            cancellationToken);

        if (data == null)
        {
            return null;
        }

        using (data.Stream)
        {
            return await BagItInfo.Parse(data.Stream, cancellationToken);
        }
    }

    private async Task<StorageServiceFileBase> StoreFileAndDispose(string filePath, FileData data, CancellationToken cancellationToken)
    {
        using (data.Stream)
        {
            return await storageService.StoreFile(filePath, data, cancellationToken);
        }
    }

    private Task AddOrUpdatePayloadManifestItem(
        DatasetVersionIdentifier datasetVersion,
        BagItManifestItem item,
        CancellationToken cancellationToken) =>
        LockAndUpdatePayloadManifest(datasetVersion, manifest => manifest.AddOrUpdateItem(item), cancellationToken);

    /*private Task AddOrUpdateFetchItem(
        DatasetVersionIdentifier datasetVersion,
        BagItFetchItem item,
        CancellationToken cancellationToken) =>
        LockAndUpdateFetch(datasetVersion, fetch => fetch.AddOrUpdateItem(item), cancellationToken);*/

    private Task RemoveItemFromPayloadManifest(
        DatasetVersionIdentifier datasetVersion,
        string filePath,
        CancellationToken cancellationToken) =>
        LockAndUpdatePayloadManifest(datasetVersion, manifest => manifest.RemoveItem(filePath), cancellationToken);

    private Task RemoveItemFromFetch(
        DatasetVersionIdentifier datasetVersion,
        string filePath,
        CancellationToken cancellationToken) =>
        LockAndUpdateFetch(datasetVersion, fetch => fetch.RemoveItem(filePath), cancellationToken);

    private async Task LockAndUpdatePayloadManifest(
        DatasetVersionIdentifier datasetVersion,
        Func<BagItManifest, bool> action,
        CancellationToken cancellationToken)
    {
        // This method assumes that there is no exlusive lock on datasetVersion

        using (await lockService.LockPath(GetManifestFilePath(datasetVersion, true), cancellationToken))
        {
            var manifest = await LoadManifest(datasetVersion, true, cancellationToken);

            if (action(manifest))
            {
                await StoreManifest(datasetVersion, true, manifest, cancellationToken);
            }
        }
    }

    private async Task LockAndUpdateFetch(
        DatasetVersionIdentifier datasetVersion,
        Func<BagItFetch, bool> action,
        CancellationToken cancellationToken)
    {
        // This method assumes that there is no exlusive lock on datasetVersion

        using (await lockService.LockPath(GetFullFilePath(datasetVersion, fetchFileName), cancellationToken))
        {
            var fetch = await LoadFetch(datasetVersion, cancellationToken);

            if (action(fetch))
            {
                await StoreFetch(datasetVersion, fetch, cancellationToken);
            }
        }
    }

    private Task StoreManifest(
        DatasetVersionIdentifier datasetVersion,
        bool payload,
        BagItManifest manifest,
        CancellationToken cancellationToken)
    {
        string filePath = GetManifestFilePath(datasetVersion, payload);

        if (manifest.Items.Any())
        {
            return StoreFileAndDispose(
                filePath,
                CreateFileDataFromByteArray(manifest.Serialize()),
                cancellationToken);
        }

        return storageService.DeleteFile(filePath, cancellationToken);
    }

    private Task StoreFetch(
        DatasetVersionIdentifier datasetVersion,
        BagItFetch fetch,
        CancellationToken cancellationToken)
    {
        string filePath = GetFetchFilePath(datasetVersion);

        if (fetch.Items.Any())
        {
            return StoreFileAndDispose(
                filePath,
                CreateFileDataFromByteArray(fetch.Serialize()),
                cancellationToken);
        }

        return storageService.DeleteFile(filePath, cancellationToken);
    }

    private Task<StorageServiceFileBase> StoreBagInfo(
        DatasetVersionIdentifier datasetVersion,
        byte[] contents,
        CancellationToken cancellationToken)
    {
        string filePath = GetBagInfoFilePath(datasetVersion);

        return StoreFileAndDispose(filePath, CreateFileDataFromByteArray(contents), cancellationToken);
    }

    private static string GetActualFilePath(DatasetVersionIdentifier datasetVersion, BagItFetch fetch, string filePath)
    {
        if (fetch.TryGetItem(filePath, out var fetchItem))
        {
            return GetDatasetPath(datasetVersion) + DecodeUrlEncodedPath(fetchItem.Url[3..]);
        }

        return GetFullFilePath(datasetVersion, filePath);
    }

    private async IAsyncEnumerable<StorageServiceFile> ListPayloadFiles(
        DatasetVersionIdentifier datasetVersion,
        FileType? type,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string path = GetDatasetVersionPath(datasetVersion);

        await foreach (var file in storageService.ListFiles(
            path + GetPayloadPath(type),
            cancellationToken))
        {
            yield return file with { Path = file.Path[path.Length..] };
        }
    }

    private static FileData CreateFileDataFromByteArray(byte[] data) =>
        new(new MemoryStream(data), data.LongLength, "text/plain");

    private async Task<bool> VersionHasBeenPublished(DatasetVersionIdentifier datasetVersion, CancellationToken cancellationToken)
    {
        var data = await storageService.GetFileData(GetBagItFilePath(datasetVersion), cancellationToken);

        if (data == null)
        {
            return false;
        }

        using (data.Stream)
        {
            return true;
        }
    }

    private async Task ThrowIfHasBeenPublished(DatasetVersionIdentifier datasetVersion, CancellationToken cancellationToken)
    {
        if (await VersionHasBeenPublished(datasetVersion, cancellationToken))
        {
            throw new DatasetStatusException();
        }
    }

    /*private static bool TryGetPreviousVersionNumber(string versionNumber, out string previousVersionNumber)
    {
        var values = versionNumber.Split('.');
        int versionMajor = int.Parse(values[0], CultureInfo.InvariantCulture);

        if (versionMajor > 1)
        {
            previousVersionNumber = (versionMajor - 1).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        previousVersionNumber = "";
        return false;
    }*/
}
