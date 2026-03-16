using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.BagIt;
using DorisStorageAdapter.Services.Implementation.BagIt.Fetch;
using DorisStorageAdapter.Services.Implementation.BagIt.Info;
using DorisStorageAdapter.Services.Implementation.BagIt.Manifest;
using DorisStorageAdapter.Services.Implementation.Configuration;
using DorisStorageAdapter.Services.Implementation.IO;
using DorisStorageAdapter.Services.Implementation.Locking;
using DorisStorageAdapter.Services.Implementation.Services.Bags;
using DorisStorageAdapter.Services.Implementation.Services.Locking;
using DorisStorageAdapter.Services.Implementation.Services.Validation;
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

namespace DorisStorageAdapter.Services.Implementation.Services;

internal sealed class FileService(
    ILockProvider lockProvider,
    DatasetVersionLocks datasetVersionLocks,
    BagContextFactory bagContextFactory,
    IOptions<PublicationConfiguration> publicationConfiguration) : IFileService
{
    private readonly ILockProvider _lockProvider = lockProvider;
    private readonly DatasetVersionLocks _datasetVersionLocks = datasetVersionLocks;
    private readonly BagContextFactory _bagContextFactory = bagContextFactory;
    private readonly PublicationConfiguration _publicationConfiguration = publicationConfiguration.Value;

    public async Task<FileMetadata> StoreAsync(
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
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        DatasetVersionValidator.ThrowIfInvalid(datasetVersion);
        ThrowIfInvalidFilePath(filePath);

        await using var datasetVersionLock = await _datasetVersionLocks
            .AcquireReadLockOrThrowAsync(datasetVersion, cancellationToken);

        await using var fileLock = await AcquireFileLockOrThrowAsync(
            datasetVersion, type, filePath, cancellationToken);

        var bagContext = _bagContextFactory.Create(datasetVersion);
        await ThrowIfHasBeenPublishedAsync(bagContext, cancellationToken);

        string pathInBag = BagPathLayout.ToPathInBag(type, filePath);
        StorageFileBaseMetadata result;
        byte[] checksum;
        long bytesRead;

        await using (var hashStream = new CountedHashStream(data))
        {
            result = await bagContext.StoreFileAsync(
                path: pathInBag,
                data: data,
                size: size,
                contentType: contentType,
                cancellationToken: cancellationToken);

            checksum = hashStream.GetHash();
            bytesRead = hashStream.BytesRead;
        }

        // Do not cancel the operation from this point on,
        // since the file has been successfully stored.

        await using (await AcquireBagStructureLockAsync(datasetVersion, CancellationToken.None))
        {
            // Remove from fetch if present there.
            await RemoveItemFromFetchAsync(bagContext, pathInBag, CancellationToken.None);
            // Update payload manifest.
            await AddOrUpdatePayloadManifestItemAsync(bagContext, new(pathInBag, checksum), CancellationToken.None);
        }

        return new(
            ContentType: result.ContentType ?? MimeTypes.GetMimeType(filePath),
            DateCreated: result.DateCreated,
            DateModified: result.DateModified,
            Path: filePath,
            Sha256: checksum,
            Size: bytesRead,
            Type: type);
    }

    public async Task DeleteAsync(
        DatasetVersion datasetVersion,
        FileType type,
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        DatasetVersionValidator.ThrowIfInvalid(datasetVersion);
        ThrowIfInvalidFilePath(filePath);

        await using var datasetVersionLock = await _datasetVersionLocks
            .AcquireReadLockOrThrowAsync(datasetVersion, cancellationToken);

        await using var fileLock = await AcquireFileLockOrThrowAsync(
            datasetVersion, type, filePath, cancellationToken);

        var bagContext = _bagContextFactory.Create(datasetVersion);
        await ThrowIfHasBeenPublishedAsync(bagContext, cancellationToken);

        string pathInBag = BagPathLayout.ToPathInBag(type, filePath);

        await bagContext.DeleteFileAsync(
            path: pathInBag,
            cancellationToken: cancellationToken);

        // Do not cancel the operation from this point on,
        // since the file has been successfully deleted.

        await using (await AcquireBagStructureLockAsync(datasetVersion, CancellationToken.None))
        {
            await RemoveItemFromPayloadManifestAsync(bagContext, pathInBag, CancellationToken.None);
            await RemoveItemFromFetchAsync(bagContext, pathInBag, CancellationToken.None);
        }
    }

    public async Task ImportAsync(
       DatasetVersion datasetVersion,
       string fromVersion,
       CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(fromVersion);
        DatasetVersionValidator.ThrowIfInvalid(datasetVersion);

        var fromDatasetVersion = new DatasetVersion(datasetVersion.Identifier, fromVersion);
        DatasetVersionValidator.ThrowIfInvalid(fromDatasetVersion);

        if (datasetVersion == fromDatasetVersion)
        {
            // Importing from and to the same version, do nothing.
            return;
        }

        var bagContext = _bagContextFactory.Create(datasetVersion);
        var fromBagContext = _bagContextFactory.Create(fromDatasetVersion);

        if (!await fromBagContext.HasBeenPublishedAsync(cancellationToken))
        {
            // fromVersion is not published, do nothing.
            return;
        }

        await using var datasetVersionLock = await _datasetVersionLocks
           .AcquireWriteLockOrThrowAsync(datasetVersion, cancellationToken);

        await ThrowIfHasBeenPublishedAsync(bagContext, cancellationToken);

        async Task<BagItFetch> PrepareFetchAsync()
        {
            var fetch = await fromBagContext.LoadBagItElementAsync<BagItFetch>(cancellationToken);

            await foreach (var file in fromBagContext.ListPayloadFilesAsync(cancellationToken))
            {
                fetch.AddOrUpdateItem(new(
                    file.Path, file.Size, bagContext.CreateFetchUrl(fromBagContext, file.Path)));
            }

            return fetch;
        }

        if (await bagContext.ListPayloadFilesAsync(cancellationToken)
            .GetAsyncEnumerator(cancellationToken).MoveNextAsync())
        {
            // Payload files present, do nothing.
            return;
        }

        var fetch = await PrepareFetchAsync();
        var manifest = await fromBagContext.LoadBagItElementAsync<BagItPayloadManifest>(cancellationToken);

        await bagContext.StoreBagItElementAsync(fetch, cancellationToken);
        await bagContext.StoreBagItElementAsync(manifest, CancellationToken.None);
    }

    public async Task<FileData?> GetDataAsync(
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
        DatasetVersionValidator.ThrowIfInvalid(datasetVersion);
        ThrowIfInvalidFilePath(filePath);

        var bagContext = _bagContextFactory.Create(datasetVersion);

        var accessibleFileTypes = await GetAccessibleFileTypesAsync(
            bagContext, allowDraft, cancellationToken);

        if (!accessibleFileTypes.Contains(type))
        {
            return null;
        }

        string pathInBag = BagPathLayout.ToPathInBag(type, filePath);

        var fetch = await bagContext.LoadBagItElementAsync<BagItFetch>(cancellationToken);
        (bagContext, pathInBag) = ResolvePath(bagContext, fetch, pathInBag);

        FileData? result = null;
        if (isHeadRequest)
        {
            var metadata = await bagContext.GetFileMetadataAsync(pathInBag, cancellationToken);

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
            var data = await bagContext.GetFileDataAsync(pathInBag, byteRange, cancellationToken);

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

    public async IAsyncEnumerable<FileMetadata> ListAsync(
        DatasetVersion datasetVersion,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        DatasetVersionValidator.ThrowIfInvalid(datasetVersion);

        var bagContext = _bagContextFactory.Create(datasetVersion);

        var payloadManifest = await bagContext.LoadBagItElementAsync<BagItPayloadManifest>(cancellationToken);
        var fetch = await bagContext.LoadBagItElementAsync<BagItFetch>(cancellationToken);

        byte[]? GetChecksum(string pathInBag) =>
            payloadManifest.TryGetItem(pathInBag, out var value)
                ? value.Checksum
                : null;

        var result = new List<StorageFileMetadata>();

        foreach (var reference in bagContext.GroupFetchReferences(fetch))
        {
            var referencedBagContext = _bagContextFactory.Create(reference.ReferencedBagStoragePath);
            var dict = reference.References.ToDictionary(r => r.PathInBag);

            await foreach (var file in referencedBagContext.ListPayloadFilesAsync(cancellationToken))
            {
                if (dict.TryGetValue(file.Path, out var r))
                {
                    result.Add(file with { Path = r.Item.FilePath });
                }
            }
        }

        await foreach (var file in bagContext.ListPayloadFilesAsync(cancellationToken))
        {
            result.Add(file);
        }

        foreach (var file in result.OrderBy(f => f.Path, StringComparer.InvariantCulture))
        {
            (FileType type, string filePath) = BagPathLayout.FromPathInBag(file.Path);

            yield return new(
                ContentType: file.ContentType ?? MimeTypes.GetMimeType(file.Path),
                DateCreated: file.DateCreated,
                DateModified: file.DateModified,
                Path: filePath,
                Sha256: GetChecksum(file.Path),
                Size: file.Size,
                Type: type);
        }
    }

    public async Task WriteDataAsZipAsync(
        DatasetVersion datasetVersion,
        string[] paths,
        Stream stream,
        bool allowDraft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(stream);
        DatasetVersionValidator.ThrowIfInvalid(datasetVersion);

        var bagContext = _bagContextFactory.Create(datasetVersion);

        var accessibleFileTypes = await GetAccessibleFileTypesAsync(
           bagContext, allowDraft, cancellationToken);

        static Stream CreateZipEntryStream(ZipArchive zipArchive, string path)
        {
            var entry = zipArchive.CreateEntry(path, CompressionLevel.NoCompression);
            return entry.Open();
        }

        var payloadManifest = await bagContext.LoadBagItElementAsync<BagItPayloadManifest>(cancellationToken);
        var fetch = await bagContext.LoadBagItElementAsync<BagItFetch>(cancellationToken);
        string versionPath = datasetVersion.Identifier + '-' + datasetVersion.Version;

        using var zipArchive = new ZipArchive(stream, ZipArchiveMode.Create, false);
        var sent = new List<BagItManifestItem>();

        foreach (var manifestItem in payloadManifest.Items)
        {
            (var type, string filePath) = BagPathLayout.FromPathInBag(manifestItem.FilePath);

            if (!accessibleFileTypes.Contains(type))
            {
                continue;
            }

            string zipFilePath = type.ToString() + '/' + filePath;

            if (paths.Length > 0 &&
                !paths.Any(p => zipFilePath.StartsWith(p, StringComparison.Ordinal)))
            {
                continue;
            }

            (var fromBagContext, string pathInBag) = ResolvePath(bagContext, fetch, manifestItem.FilePath);
            var fileData = await fromBagContext.GetFileDataAsync(pathInBag, null, cancellationToken);

            if (fileData != null)
            {
                await using var entryStream = CreateZipEntryStream(zipArchive, versionPath + '/' + zipFilePath);
                await using (fileData.Stream)
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

            await using var entryStream = CreateZipEntryStream(zipArchive, versionPath + "/sha256.txt");

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

    private static void ThrowIfInvalidFilePath(string filePath)
    {
        foreach (string pathComponent in filePath.Split('/'))
        {
            if (string.IsNullOrEmpty(pathComponent) ||
                pathComponent is "." or "..")
            {
                throw new ValidationException([new("Invalid path.")]);
            }
        }
    }

    private (BagContext BagContext, string PathInBag) ResolvePath(BagContext bagContext, BagItFetch fetch, string pathInBag)
    {
        if (fetch.TryGetItem(pathInBag, out var item))
        {
            var reference = bagContext.ParseFetchReference(item);
            return (_bagContextFactory.Create(reference.ReferencedBagStoragePath), reference.PathInBag);
        }

        return (bagContext, pathInBag);
    }

    private static async Task ThrowIfHasBeenPublishedAsync(BagContext bagContext, CancellationToken cancellationToken)
    {
        if (await bagContext.HasBeenPublishedAsync(cancellationToken))
        {
            throw new DatasetStatusException();
        }
    }

    private async Task<IEnumerable<FileType>> GetAccessibleFileTypesAsync(
        BagContext bagContext, bool allowDraft, CancellationToken cancellationToken)
    {
        if (await bagContext.HasBeenPublishedAsync(cancellationToken))
        {
            var bagInfo = await bagContext.LoadBagItElementAsync<BagItInfo>(cancellationToken);

            // Check that version is not withdrawn.
            if (bagInfo.GetDatasetVersionStatus() != DatasetVersionStatus.published)
            {
                return [];
            }

            // If we reach here, we know we can allow access to documentation files.
            // Check if we also allow access to data files.
            if (_publicationConfiguration.AllowPublicAccessRight &&
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

    private static Task AddOrUpdatePayloadManifestItemAsync(
        BagContext bagContext,
        BagItManifestItem item,
        CancellationToken cancellationToken) =>
        UpdateBagItElementAsync<BagItPayloadManifest>(
            bagContext, manifest => manifest.AddOrUpdateItem(item), cancellationToken);

    private static Task RemoveItemFromPayloadManifestAsync(
        BagContext bagContext,
        string pathInBag,
        CancellationToken cancellationToken) =>
        UpdateBagItElementAsync<BagItPayloadManifest>(
            bagContext, manifest => manifest.RemoveItem(pathInBag), cancellationToken);

    private static Task RemoveItemFromFetchAsync(
        BagContext bagContext,
        string pathInBag,
        CancellationToken cancellationToken) =>
        UpdateBagItElementAsync<BagItFetch>(bagContext, fetch => fetch.RemoveItem(pathInBag), cancellationToken);

    private static async Task UpdateBagItElementAsync<T>(
        BagContext bagContext,
        Func<T, bool> action,
        CancellationToken cancellationToken)
        where T : IBagItElement<T>
    {
        var element = await bagContext.LoadBagItElementAsync<T>(cancellationToken);

        if (action(element))
        {
            await bagContext.StoreBagItElementAsync(element, cancellationToken);
        }
    }

    private async ValueTask<IAsyncDisposable> AcquireFileLockOrThrowAsync(
       DatasetVersion datasetVersion,
       FileType type,
       string filePath,
       CancellationToken cancellationToken)
    {
        return await _lockProvider.TryAcquireAsync(
            LockKeys.DatasetVersionFile(datasetVersion, type, filePath), cancellationToken) ??
            throw new ConflictException();
    }

    private ValueTask<IAsyncDisposable> AcquireBagStructureLockAsync(
        DatasetVersion datasetVersion,
        CancellationToken cancellationToken) =>
        _lockProvider.AcquireAsync(LockKeys.BagStructure(datasetVersion), cancellationToken);
}
