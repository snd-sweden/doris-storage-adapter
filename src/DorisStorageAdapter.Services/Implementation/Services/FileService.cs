using DorisStorageAdapter.BagIt;
using DorisStorageAdapter.BagIt.Fetch;
using DorisStorageAdapter.BagIt.Info;
using DorisStorageAdapter.BagIt.Manifest;
using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.Configuration;
using DorisStorageAdapter.Services.Implementation.IO;
using DorisStorageAdapter.Services.Implementation.Locking;
using DorisStorageAdapter.Services.Implementation.Services.Audit;
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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services;

internal sealed class FileService(
    ILockProvider lockProvider,
    DatasetVersionValidator datasetVersionValidator,
    DatasetVersionLocks datasetVersionLocks,
    BagContextFactory bagContextFactory,
    IOptions<SystemConfiguration> systemConfiguration,
    AuditedOperationRunner audit) : IFileService
{
    private readonly ILockProvider _lockProvider = lockProvider;
    private readonly DatasetVersionLocks _datasetVersionLocks = datasetVersionLocks;
    private readonly DatasetVersionValidator _datasetVersionValidator = datasetVersionValidator;
    private readonly BagContextFactory _bagContextFactory = bagContextFactory;
    private readonly SystemConfiguration _systemConfiguration = systemConfiguration.Value;
    private readonly AuditedOperationRunner _audit = audit;

    public const string UploadMarkerFilePrefix = "_upload-";

    public Task<FileMetadata> StoreAsync(
        DatasetVersion datasetVersion,
        string filePath,
        Stream data,
        long size,
        CancellationToken cancellationToken) =>
        _audit.RunAsync(
            new AuditOperation
            {
                Action = "UploadFile",
                DatasetVersion = datasetVersion,
                Target = filePath,

                Details = new Dictionary<string, object>
                {
                    ["Size"] = size
                }
            },
            async (state, ct) =>
            {
                var result = await StoreCoreAsync(
                    datasetVersion,
                    filePath,
                    data,
                    size,
                    ct);

                if (result.Sha256 != null)
                {
                    state.Details["Sha256"] = 
                        Convert.ToHexStringLower(result.Sha256);
                }

                return result;
            },
            cancellationToken);

    public async Task<FileMetadata> StoreCoreAsync(
        DatasetVersion datasetVersion,
        string filePath,
        Stream data,
        long size,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        _datasetVersionValidator.ThrowIfInvalid(datasetVersion);
        ThrowIfInvalidFilePath(filePath);

        await using var datasetVersionLock = await _datasetVersionLocks
            .AcquireSharedLockOrThrowAsync(datasetVersion, cancellationToken);

        await using var fileLock = await AcquireFileLockOrThrowAsync(
            datasetVersion, filePath, cancellationToken);

        var bagContext = _bagContextFactory.Create(datasetVersion);
        await ThrowIfHasBeenPublishedAsync(bagContext, cancellationToken);

        string pathInBag = BagPathLayout.ToPathInBag(filePath);

        // Store marker file indicating upload is in progress.
        // Used to detect unfinished uploads.
        string markerFileName = GetUploadMarkerFileName(pathInBag);
        bool markerFileAlreadyExists = await bagContext.GetFileMetadataAsync(markerFileName, cancellationToken) != null;
        using (var markerFileContent = new MemoryStream(Encoding.UTF8.GetBytes(pathInBag)))
        {
            await bagContext.StoreFileAsync(
                path: markerFileName,
                data: markerFileContent,
                size: markerFileContent.Length,
                cancellationToken: cancellationToken);
        }

        byte[] checksum;
        long bytesRead;

        try
        {
            await using var hashStream = new CountedHashStream(data, leaveOpen: true);

            await bagContext.StoreFileAsync(
                path: pathInBag,
                data: hashStream,
                size: size,
                cancellationToken: cancellationToken);

            checksum = hashStream.GetHash();
            bytesRead = hashStream.BytesRead;
        }
        catch when (!markerFileAlreadyExists)
        {
            // Cancelled or failed, try deleting marker file (only if it did not already exist
            // when entering method).
            try
            {
                await bagContext.DeleteFileAsync(markerFileName, CancellationToken.None);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch { }
#pragma warning restore CA1031

            throw;
        }

        // Do not cancel the operation from this point on,
        // since the file has been successfully stored.

        await using (await AcquireBagStructureLockAsync(datasetVersion, CancellationToken.None))
        {
            // Remove from fetch if present there.
            await RemoveItemFromFetchAsync(bagContext, pathInBag, CancellationToken.None);
            // Update payload manifest.
            await AddOrUpdatePayloadManifestItemAsync(bagContext, new(pathInBag, new(checksum)), CancellationToken.None);
        }

        // Delete file marking that upload is in progress.
        await bagContext.DeleteFileAsync(markerFileName, CancellationToken.None);

        return new(
            ContentType: GetMimeType(filePath),
            DateCreated: null,
            DateModified: null,
            Path: filePath,
            Sha256: checksum,
            Size: bytesRead);
    }

    public Task DeleteAsync(
        DatasetVersion datasetVersion,
        string filePath,
        CancellationToken cancellationToken) =>
        _audit.RunAsync(
            new AuditOperation
            {
                Action = "DeleteFile",
                DatasetVersion = datasetVersion,
                Target = filePath
            },
            (_, ct) =>
                DeleteCoreAsync(
                    datasetVersion,
                    filePath,
                    ct),
            cancellationToken);

    public async Task DeleteCoreAsync(
        DatasetVersion datasetVersion,
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        _datasetVersionValidator.ThrowIfInvalid(datasetVersion);
        ThrowIfInvalidFilePath(filePath);

        await using var datasetVersionLock = await _datasetVersionLocks
            .AcquireSharedLockOrThrowAsync(datasetVersion, cancellationToken);

        await using var fileLock = await AcquireFileLockOrThrowAsync(
            datasetVersion, filePath, cancellationToken);

        var bagContext = _bagContextFactory.Create(datasetVersion);
        await ThrowIfHasBeenPublishedAsync(bagContext, cancellationToken);

        string pathInBag = BagPathLayout.ToPathInBag(filePath);

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

        // Delete file marking that upload is in progress.
        await bagContext.DeleteFileAsync(GetUploadMarkerFileName(pathInBag), CancellationToken.None);
    }

    public Task ImportAsync(
        DatasetVersion datasetVersion,
        string fromVersion,
        CancellationToken cancellationToken) =>
        _audit.RunAsync(
            new AuditOperation
            {
                Action = "ImportFiles",
                DatasetVersion = datasetVersion,

                Details = new Dictionary<string, object>
                {
                    ["FromVersion"] = fromVersion
                }
            },
            (_, ct) =>
                ImportCoreAsync(
                    datasetVersion,
                    fromVersion,
                    ct),
           cancellationToken);

    public async Task ImportCoreAsync(
       DatasetVersion datasetVersion,
       string fromVersion,
       CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(fromVersion);
        _datasetVersionValidator.ThrowIfInvalid(datasetVersion);

        var fromDatasetVersion = new DatasetVersion(
            datasetVersion.Identifier, fromVersion, datasetVersion.TenantId);
        _datasetVersionValidator.ThrowIfInvalid(fromDatasetVersion);

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
           .AcquireExclusiveLockOrThrowAsync(datasetVersion, cancellationToken);

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
        string filePath,
        FileAccessScope scope,
        ByteRange? byteRange,
        CancellationToken cancellationToken)
    {
        if (scope == FileAccessScope.Public)
        {
            // Do not audit public downloads.

            return await GetDataCoreAsync(
                datasetVersion,
                filePath,
                scope,
                byteRange,
                cancellationToken);
        }

        var details = new Dictionary<string, object>
        {
            ["AccessScope"] = scope
        };

        if (byteRange != null)
        {
            details["ByteRange"] = $"{byteRange.From}-{byteRange.To}";
        }

        return await _audit.StartAsync(
            new AuditOperation
            {
                Action = "DownloadFile",
                DatasetVersion = datasetVersion,
                Target = filePath,
                Details = details
            },
            async (auditHandle, ct) =>
            {
                var fileData = await GetDataCoreAsync(
                    datasetVersion,
                    filePath,
                    scope,
                    byteRange,
                    ct);

                if (fileData == null)
                {
                    return null;
                }

                auditHandle.State.Details["TotalSize"] = fileData.Size;
                auditHandle.State.Details["ReturnedSize"] = fileData.StreamLength;

                return fileData with
                { 
                    Stream = new AuditedReadStream(
                        fileData.Stream, 
                        auditHandle,
                        fileData.StreamLength)
                };
            },
            cancellationToken);
    }

    public async Task<FileData?> GetDataCoreAsync(
        DatasetVersion datasetVersion,
        string filePath,
        FileAccessScope scope,
        ByteRange? byteRange,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        if (!IsValidFilePath(filePath))
        {
            return null;
        }

        if (!_datasetVersionValidator.IsValid(datasetVersion))
        {
            return null;
        }

        var bagContext = _bagContextFactory.Create(datasetVersion);

        bool allow = await IsReadAccessToFilesAllowedAsync(
            bagContext, scope, cancellationToken);

        if (!allow)
        {
            return null;
        }

        string pathInBag = BagPathLayout.ToPathInBag(filePath);

        var fetch = await bagContext.LoadBagItElementAsync<BagItFetch>(cancellationToken);
        (bagContext, pathInBag) = ResolvePath(bagContext, fetch, pathInBag);

        var data = await bagContext.GetFileDataAsync(
            pathInBag, byteRange, cancellationToken);

        if (data != null)
        {
            return new(
                ContentType: GetMimeType(filePath),
                Size: data.Size,
                Stream: data.Stream,
                StreamLength: data.StreamLength);
        }

        return null;
    }

    public async Task<FileMetadata?> GetMetaDataAsync(
        DatasetVersion datasetVersion,
        string filePath,
        FileAccessScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        if (!IsValidFilePath(filePath))
        {
            return null;
        }

        if (!_datasetVersionValidator.IsValid(datasetVersion))
        {
            return null;
        }

        var bagContext = _bagContextFactory.Create(datasetVersion);

        bool allow = await IsReadAccessToFilesAllowedAsync(
            bagContext, scope, cancellationToken);

        if (!allow)
        {
            return null;
        }

        string pathInBag = BagPathLayout.ToPathInBag(filePath);

        var fetch = await bagContext.LoadBagItElementAsync<BagItFetch>(cancellationToken);
        (bagContext, pathInBag) = ResolvePath(bagContext, fetch, pathInBag);

        var metadata = await bagContext.GetFileMetadataAsync(pathInBag, cancellationToken);

        if (metadata != null)
        {
            return new(
                ContentType: GetMimeType(filePath),
                DateCreated: metadata.DateCreated,
                DateModified: metadata.DateModified,
                Sha256: null,
                Path: filePath,
                Size: metadata.Size);
        }

        return null;
    }

    public async IAsyncEnumerable<FileMetadata> ListAsync(
        DatasetVersion datasetVersion,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        _datasetVersionValidator.ThrowIfInvalid(datasetVersion);

        var bagContext = _bagContextFactory.Create(datasetVersion);

        var payloadManifest = await bagContext.LoadBagItElementAsync<BagItPayloadManifest>(cancellationToken);
        var fetch = await bagContext.LoadBagItElementAsync<BagItFetch>(cancellationToken);

        Checksum? GetChecksum(string pathInBag) =>
            payloadManifest.TryGetItem(pathInBag, out var value)
                ? value.Checksum
                : null;

        var result = new List<StorageFileMetadata>();

        foreach (var referenceGroup in bagContext.GroupFetchReferences(fetch))
        {
            var referencedBagContext = _bagContextFactory.Create(referenceGroup.ReferencedBagStoragePath);
            var referencesByPath = referenceGroup.References.ToDictionary(r => r.PathInBag, StringComparer.Ordinal);

            await foreach (var file in referencedBagContext.ListPayloadFilesAsync(cancellationToken))
            {
                if (referencesByPath.TryGetValue(file.Path, out var reference))
                {
                    result.Add(file with { Path = reference.Item.FilePath });
                }
            }
        }

        await foreach (var file in bagContext.ListPayloadFilesAsync(cancellationToken))
        {
            result.Add(file);
        }

        foreach (var file in result.OrderBy(f => f.Path, StringComparer.InvariantCulture))
        {
            yield return new(
                ContentType: GetMimeType(file.Path),
                DateCreated: file.DateCreated,
                DateModified: file.DateModified,
                Path: BagPathLayout.FromPathInBag(file.Path),
                Sha256: GetChecksum(file.Path)?.Bytes.ToArray(),
                Size: file.Size);
        }
    }

    public Task<bool> TryWriteDataAsZipAsync(
        DatasetVersion datasetVersion,
        string[] paths,
        Stream stream,
        FileAccessScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (scope == FileAccessScope.Public)
        {
            // Do not audit public downloads.

            return TryWriteDataAsZipCoreAsync(
                datasetVersion,
                paths,
                stream,
                scope,
                new(),
                cancellationToken);
        }

        var details = new Dictionary<string, object>()
        {
            ["AccessScope"] = scope,
        };

        for (int i = 0; i < paths.Length; i++)
        {
            details[$"PathPrefix.{i}"] = paths[i];
        }

        return _audit.StartAsync(
            new AuditOperation
            {
                Action = "DownloadZip",
                DatasetVersion = datasetVersion,
                Details = details
            },
            async (auditHandle, ct) =>
            {
                bool written = await TryWriteDataAsZipCoreAsync(
                    datasetVersion,
                    paths,
                    stream,
                    scope,
                    auditHandle.State,
                    cancellationToken);

                if (written)
                {
                    await auditHandle.CompleteAsync(AuditOutcome.Success);
                }

                return written;
            },
           cancellationToken);
    }

    public async Task<bool> TryWriteDataAsZipCoreAsync(
        DatasetVersion datasetVersion,
        string[] paths,
        Stream stream,
        FileAccessScope scope,
        AuditExecutionState auditState, 
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(stream);

        if (!_datasetVersionValidator.IsValid(datasetVersion))
        {
            return false;
        }

        var bagContext = _bagContextFactory.Create(datasetVersion);

        bool allowed = await IsReadAccessToFilesAllowedAsync(
           bagContext, scope, cancellationToken);

        if (!allowed)
        {
            return false;
        }

        var sent = new List<BagItManifestItem>();
        long totalBytesRead = 0;

        void UpdateAuditCounts()
        {
            auditState.Details["CompletedFileCount"] = sent.Count;
            auditState.Details["BytesRead"] = totalBytesRead;
        }

        UpdateAuditCounts();

        var payloadManifest = await bagContext
            .LoadBagItElementAsync<BagItPayloadManifest>(cancellationToken);

        string versionPath = datasetVersion.Identifier + '-' + datasetVersion.Version;
        var toSend = new List<(BagItManifestItem ManifestItem, string ZipFilePath)>();

        foreach (var manifestItem in payloadManifest.Items)
        {
            string filePath = BagPathLayout.FromPathInBag(manifestItem.FilePath);

            if (paths.Length == 0 ||
                paths.Any(p => filePath.StartsWith(p, StringComparison.Ordinal)))
            {
                toSend.Add((manifestItem, versionPath + '/' + filePath));
            }
        }

        auditState.Details["SelectedFileCount"] = toSend.Count;

        static Stream CreateZipEntryStream(ZipArchive zipArchive, string path)
        {
            var entry = zipArchive.CreateEntry(path, CompressionLevel.NoCompression);
            return entry.Open();
        }

        var fetch = await bagContext.LoadBagItElementAsync<BagItFetch>(cancellationToken);
        using var zipArchive = new ZipArchive(stream, ZipArchiveMode.Create, false);
        
        foreach (var (manifestItem, zipFilePath) in toSend)
        {
            (var fromBagContext, string pathInBag) = ResolvePath(bagContext, fetch, manifestItem.FilePath);
            var fileData = await fromBagContext.GetFileDataAsync(pathInBag, null, cancellationToken);

            if (fileData != null)
            {
                await using var entryStream = CreateZipEntryStream(zipArchive, zipFilePath);
                await using var fileStream = fileData.Stream;
                await fileStream.CopyToAsync(entryStream, cancellationToken);

                sent.Add(new(zipFilePath, manifestItem.Checksum));
                totalBytesRead += fileData.Size;

                UpdateAuditCounts();
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

            await using var entryStream = CreateZipEntryStream(zipArchive, "sha256.txt");

            foreach (var file in sent)
            {
                string name = file.FilePath
                    .Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\n", "\\n", StringComparison.Ordinal);

                await entryStream.WriteAsync(Encoding.UTF8.GetBytes(
                    (name.Length > file.FilePath.Length
                        ? "\\"
                        : "") +
                    file.Checksum.HexString + ' ' + name + '\n'),
                    cancellationToken);
            }
        }

        return true;
    }

    private static bool IsValidFilePath(string filePath) =>
        PathValidation.HasOnlyValidComponents(filePath);

    private static string GetMimeType(string filePath) =>
        MimeTypes.GetMimeType(filePath);

    private static void ThrowIfInvalidFilePath(
        string filePath,
        [CallerArgumentExpression(nameof(filePath))] string? paramName = null)
    {
        if (!IsValidFilePath(filePath))
        {
            throw new ValidationException([new(
                Target: paramName,
                Message: "Invalid path.")]);
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

    private async Task<bool> IsReadAccessToFilesAllowedAsync(
        BagContext bagContext, FileAccessScope scope, CancellationToken cancellationToken)
    {
        switch (scope)
        {
            case FileAccessScope.Public:
                if (_systemConfiguration.DatasetAccessMode == DatasetAccessMode.Open &&
                    await bagContext.HasBeenPublishedAsync(cancellationToken))
                {
                    var bagInfo = await bagContext.LoadBagItElementAsync<BagItInfo>(cancellationToken);

                    return bagInfo.GetAccessRight() == AccessRight.Public &&
                           bagInfo.GetDatasetVersionStatus() == DatasetVersionStatus.Published;
                }

                break;

            case FileAccessScope.Draft:
                return !await bagContext.HasBeenPublishedAsync(cancellationToken);
        }

        return false;
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
       string filePath,
       CancellationToken cancellationToken)
    {
        return await _lockProvider.TryAcquireAsync(
            LockKeys.DatasetVersionFile(datasetVersion, filePath), cancellationToken) ??
            throw new ConflictException();
    }

    private ValueTask<IAsyncDisposable> AcquireBagStructureLockAsync(
        DatasetVersion datasetVersion,
        CancellationToken cancellationToken) =>
        _lockProvider.AcquireAsync(LockKeys.BagStructure(datasetVersion), cancellationToken);

    private static string GetUploadMarkerFileName(string filePath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(filePath));
        return UploadMarkerFilePrefix + Convert.ToHexStringLower(hash);
    }
}
