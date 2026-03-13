using ByteSizeLib;
using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.BagIt;
using DorisStorageAdapter.Services.Implementation.BagIt.Fetch;
using DorisStorageAdapter.Services.Implementation.BagIt.Info;
using DorisStorageAdapter.Services.Implementation.BagIt.Manifest;
using DorisStorageAdapter.Services.Implementation.Configuration;
using DorisStorageAdapter.Services.Implementation.Services.Bags;
using DorisStorageAdapter.Services.Implementation.Services.Locking;
using DorisStorageAdapter.Services.Implementation.Storage;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services;

internal sealed class StatusService(
    DatasetVersionLocks datasetVersionLocks,
    BagContextFactory bagContextFactory,
    IOptions<StorageConfiguration> storageConfiguration) : IStatusService
{
    private readonly DatasetVersionLocks _datasetVersionLocks = datasetVersionLocks;
    private readonly BagContextFactory _bagContextFactory = bagContextFactory;
    private readonly StorageConfiguration _storageConfiguration = storageConfiguration.Value;

    private static readonly byte[] _bagItSha256 = SHA256.HashData(BagItDeclaration.CreateEmpty().Serialize());

    public async Task PublishAsync(
        DatasetVersion datasetVersion,
        AccessRight accessRight,
        string canonicalDoi,
        string doi,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(doi);
        Validation.ThrowIfInvalidDatasetVersion(datasetVersion);

        if (accessRight == AccessRight.@public &&
            !_storageConfiguration.AllowPublicAccessRight)
        {
            throw new PublicAccessRightNotAllowedException();
        }

        var bagContext = _bagContextFactory.Create(datasetVersion);

        await using var datasetVersionLock = await _datasetVersionLocks
            .AcquireWriteLockOrThrowAsync(datasetVersion, cancellationToken);

        if (await bagContext.HasBeenPublishedAsync(cancellationToken))
        {
            throw new DatasetStatusException();
        }

        var fetch = await bagContext.LoadBagItElementWithChecksumAsync<BagItFetch>(cancellationToken);

        var payloadFilePaths = new HashSet<string>();
        long octetCount = 0;
        bool payloadFileFound = false;
        await foreach (var file in bagContext.ListPayloadFilesAsync(cancellationToken))
        {
            payloadFilePaths.Add(file.Path);
            payloadFileFound = true;
            octetCount += file.Size;
        }
        foreach (var item in fetch?.BagItElement?.Items ?? [])
        {
            payloadFileFound = true;
            if (item.Length != null)
            {
                octetCount += item.Length.Value;
            }
        }

        if (!payloadFileFound)
        {
            // No payload files found, abort.
            return;
        }

        var payloadManifest = await bagContext
            .LoadBagItElementWithChecksumAsync<BagItPayloadManifest>(cancellationToken);

        var errors = await CheckBagConsistencyAsync(
            bagContext,
            payloadFilePaths,
            fetch?.BagItElement,
            payloadManifest?.BagItElement,
            cancellationToken);

        if (errors.Any())
        {
            throw new DatasetInconsistentException(errors);
        }

        var bagInfo = new BagItInfo
        {
            BaggingDate = DateTime.UtcNow,
            BagGroupIdentifier = canonicalDoi,
            BagSize = ByteSize.FromBytes(octetCount).ToBinaryString(CultureInfo.InvariantCulture),
            ExternalIdentifier = [doi],
            InternalSenderIdentifier = [datasetVersion.Identifier + '-' + datasetVersion.Version],
            PayloadOxum = new(octetCount, payloadManifest?.BagItElement?.Items?.LongCount() ?? 0),
        };

        bagInfo.SetAccessRight(accessRight);
        bagInfo.SetDatasetVersionStatus(DatasetVersionStatus.published);
        bagInfo.SetVersion(datasetVersion.Version);

        byte[] bagInfoContents = await bagContext.StoreBagItElementAsync(bagInfo, cancellationToken);

        // Add bagit.txt, bag-info.txt and manifest-sha256.txt to tagmanifest-sha256.txt.
        var tagManifest = await bagContext.LoadBagItElementAsync<BagItTagManifest>(CancellationToken.None);
        tagManifest.AddOrUpdateItem(new(BagItDeclaration.FileName, _bagItSha256));
        tagManifest.AddOrUpdateItem(new(BagItInfo.FileName, SHA256.HashData(bagInfoContents)));
        if (payloadManifest != null)
        {
            tagManifest.AddOrUpdateItem(new(BagItPayloadManifest.FileName, payloadManifest.Value.Checksum));
        }
        if (fetch != null)
        {
            tagManifest.AddOrUpdateItem(new(BagItFetch.FileName, fetch.Value.Checksum));
        }

        await bagContext.StoreBagItElementAsync(tagManifest, CancellationToken.None);
        await bagContext.StoreBagItElementAsync(BagItDeclaration.CreateEmpty(), CancellationToken.None);
    }

    private async Task<IEnumerable<ErrorItem>> CheckBagConsistencyAsync(
        BagContext bagContext,
        HashSet<string> payloadFilePaths,
        BagItFetch? fetch,
        BagItPayloadManifest? payloadManifest,
        CancellationToken cancellationToken)
    {
        var errors = new List<ErrorItem>();

        void AddError(string target, string message) =>
            errors.Add(new ErrorItem(message, target));

        void CheckPayloadFilePaths()
        {
            foreach (var filePath in payloadFilePaths)
            {
                string target = $"Payload directory:{filePath}";

                if (fetch != null &&
                    fetch.Contains(filePath))
                {
                    AddError(target, "Found in fetch file.");
                }

                if (payloadManifest == null ||
                    !payloadManifest.Contains(filePath))
                {
                    AddError(target, "Not found in payload manifest.");
                }
            }
        }

        async Task CheckFetchAsync()
        {
            Dictionary<string, StorageFileMetadata> referencedVersionFiles = new(StringComparer.Ordinal);
            BagItPayloadManifest? referencedVersionManifest = null;
            bool referencedVersionIsPublished = false;
            string previousPath = "";

            foreach (var item in (fetch?.Items ?? []).OrderBy(i => i.Url, StringComparer.Ordinal))
            {
                (string bagStoragePath, string pathInBag) = Paths.ResolveFetchUrl(bagContext.GroupStoragePath, item.Url);
                var referencedBagContext = _bagContextFactory.Create(bagStoragePath);

                if (referencedBagContext.StoragePath != previousPath)
                {
                    referencedVersionManifest = await referencedBagContext
                        .LoadBagItElementAsync<BagItPayloadManifest>(cancellationToken);

                    referencedVersionFiles = [];
                    await foreach (var file in referencedBagContext.ListPayloadFilesAsync(cancellationToken))
                    {
                        referencedVersionFiles[file.Path] = file;
                    }

                    referencedVersionIsPublished =
                        await referencedBagContext.HasBeenPublishedAsync(cancellationToken);

                    previousPath = referencedBagContext.StoragePath;
                }

                string target = $"Fetch file:{item.FilePath}";

                if (!referencedVersionIsPublished)
                {
                    AddError(target, "Does not reference a published dataset version.");
                }

                if (item.Length == null)
                {
                    AddError(target, "Missing length.");
                }

                if (!referencedVersionFiles.TryGetValue(pathInBag, out var referencedFile))
                {
                    AddError(target, "Referenced payload file not found.");
                }
                else if (
                    item.Length != null &&
                    item.Length != referencedFile.Size)
                {
                    AddError(target, "Size does not match referenced payload file's size.");
                }

                if (payloadManifest == null ||
                    !payloadManifest.TryGetItem(item.FilePath, out var itemThisManifest))
                {
                    AddError(target, "Not found in payload manifest.");
                }
                else if (
                    referencedVersionManifest == null ||
                    !referencedVersionManifest.TryGetItem(pathInBag, out var itemPreviousManifest) ||
                    !itemThisManifest.Checksum.SequenceEqual(itemPreviousManifest.Checksum))
                {
                    AddError(target, "Payload manifest checksum does not match referenced file's payload manifest checksum.");
                }
            }
        }

        void CheckPayloadManifest()
        {
            foreach (var item in payloadManifest?.Items ?? [])
            {
                string target = $"Payload manifest:{item.FilePath}";

                if ((fetch == null ||
                    !fetch.Contains(item.FilePath))
                    && !payloadFilePaths.Contains(item.FilePath))
                {
                    AddError(target, "Not found in payload directory or fetch file.");
                }
            }
        }

        CheckPayloadFilePaths();
        await CheckFetchAsync();
        CheckPayloadManifest();

        return errors;
    }

    public async Task SetStatusAsync(
        DatasetVersion datasetVersion,
        DatasetVersionStatus status,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        Validation.ThrowIfInvalidDatasetVersion(datasetVersion);

        var bagContext = _bagContextFactory.Create(datasetVersion);

        await using var datasetVersionLock = await _datasetVersionLocks
            .AcquireWriteLockOrThrowAsync(datasetVersion, cancellationToken);

        if (!await bagContext.HasBeenPublishedAsync(cancellationToken))
        {
            throw new DatasetStatusException();
        }

        var bagInfo = await bagContext.LoadBagItElementAsync<BagItInfo>(cancellationToken);

        if (!bagInfo.HasValues())
        {
            // Throw exception here?
            return;
        }

        if (bagInfo.GetDatasetVersionStatus() == status)
        {
            // Status is already correct, nothing to do.
            return;
        }

        bagInfo.SetDatasetVersionStatus(status);
        byte[] bagInfoContents = await bagContext.StoreBagItElementAsync(bagInfo, cancellationToken);

        var tagManifest = await bagContext.LoadBagItElementAsync<BagItTagManifest>(CancellationToken.None);
        tagManifest.AddOrUpdateItem(new(BagItInfo.FileName, SHA256.HashData(bagInfoContents)));
        await bagContext.StoreBagItElementAsync(tagManifest, CancellationToken.None);
    }
}
