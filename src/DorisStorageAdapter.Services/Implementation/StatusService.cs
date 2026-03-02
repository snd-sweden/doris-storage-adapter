using ByteSizeLib;
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
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation;

internal sealed class StatusService(
    IReaderWriterLockProvider lockProvider,
    IBagProvider bagProvider,
    IOptions<StorageConfiguration> storageConfiguration) : IStatusService
{
    private readonly IReaderWriterLockProvider _lockProvider = lockProvider;
    private readonly IBagProvider _bagProvider = bagProvider;
    private readonly StorageConfiguration _storageConfiguration = storageConfiguration.Value;

    private static readonly byte[] bagItSha256 = SHA256.HashData(BagItDeclaration.Instance.Serialize());

    public async Task Publish(
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

        var bag = _bagProvider.Create(datasetVersion);

        await using var writeLock = await TryAcquireWriteLockAsync(datasetVersion, cancellationToken);

        if (await bag.HasBeenPublished(cancellationToken))
        {
            throw new DatasetStatusException();
        }

        await PublishImpl(datasetVersion, bag, accessRight, canonicalDoi, doi, cancellationToken);
    }

    private async Task PublishImpl(
        DatasetVersion datasetVersion,
        Bag bag,
        AccessRight accessRight,
        string canonicalDoi,
        string doi,
        CancellationToken cancellationToken)
    {
        var fetch = await bag.LoadBagItElementWithChecksum<BagItFetch>(cancellationToken);

        var payloadFilePaths = new HashSet<string>();
        long octetCount = 0;
        bool payloadFileFound = false;
        await foreach (var file in bag.ListPayloadFiles(cancellationToken))
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
            // No payload files found, abort
            return;
        }

        var payloadManifest = await bag
            .LoadBagItElementWithChecksum<BagItPayloadManifest>(cancellationToken);

        var errors = await CheckBagConsistency(
            bag,
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

        byte[] bagInfoContents = await bag.StoreBagItElement(bagInfo, cancellationToken);

        // Add bagit.txt, bag-info.txt and manifest-sha256.txt to tagmanifest-sha256.txt
        var tagManifest = await bag.LoadBagItElement<BagItTagManifest>(CancellationToken.None);
        tagManifest.AddOrUpdateItem(new(BagItDeclaration.FileName, bagItSha256));
        tagManifest.AddOrUpdateItem(new(BagItInfo.FileName, SHA256.HashData(bagInfoContents)));
        if (payloadManifest != null)
        {
            tagManifest.AddOrUpdateItem(new(BagItPayloadManifest.FileName, payloadManifest.Value.Checksum));
        }
        if (fetch != null)
        {
            tagManifest.AddOrUpdateItem(new(BagItFetch.FileName, fetch.Value.Checksum));
        }

        await bag.StoreBagItElement(tagManifest, CancellationToken.None);
        await bag.StoreBagItElement(BagItDeclaration.Instance, CancellationToken.None);
    }

    private async Task<IEnumerable<ErrorItem>> CheckBagConsistency(
        Bag bag,
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

        async Task CheckFetch()
        {
            Dictionary<string, StorageFileMetadata> referencedVersionFiles = new(StringComparer.Ordinal);
            BagItPayloadManifest? referencedVerisonManifest = null;
            bool referencedVersionIsPublished = false;
            string previousPath = "";

            foreach (var item in (fetch?.Items ?? []).OrderBy(i => i.Url, StringComparer.Ordinal))
            {
                (string versionPath, string referencedFilePath) = Paths.ParseFetchUrl(item.Url);
                var referencedBag = _bagProvider.Create(bag.BagGroupPath + versionPath);

                if (referencedBag.Path != previousPath)
                {
                    referencedVerisonManifest = await referencedBag
                        .LoadBagItElement<BagItPayloadManifest>(cancellationToken);

                    referencedVersionFiles = [];
                    await foreach (var file in referencedBag.ListPayloadFiles(cancellationToken))
                    {
                        referencedVersionFiles[file.Path] = file;
                    }

                    referencedVersionIsPublished =
                        await referencedBag.HasBeenPublished(cancellationToken);

                    previousPath = referencedBag.Path;
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

                if (!referencedVersionFiles.TryGetValue(referencedFilePath, out var referencedFile))
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
                    referencedVerisonManifest == null ||
                    !referencedVerisonManifest.TryGetItem(referencedFilePath, out var itemPreviousManifest) ||
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
        await CheckFetch();
        CheckPayloadManifest();

        return errors;
    }

    public async Task SetStatus(
        DatasetVersion datasetVersion,
        DatasetVersionStatus status,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        Validation.ThrowIfInvalidDatasetVersion(datasetVersion);

        var bag = _bagProvider.Create(datasetVersion);

        await using var writeLock = await TryAcquireWriteLockAsync(datasetVersion, cancellationToken);

        if (!await bag.HasBeenPublished(cancellationToken))
        {
            throw new DatasetStatusException();
        }

        await SetStatusImpl(bag, status, cancellationToken);
    }

    private static async Task SetStatusImpl(
        Bag bag,
        DatasetVersionStatus status,
        CancellationToken cancellationToken)
    {
        var bagInfo = await bag.LoadBagItElement<BagItInfo>(cancellationToken);

        if (!bagInfo.HasValues())
        {
            // Throw exception here?
            return;
        }

        if (bagInfo.GetDatasetVersionStatus() == status)
        {
            // Status is already correct, nothing to do
            return;
        }

        bagInfo.SetDatasetVersionStatus(status);
        byte[] bagInfoContents = await bag.StoreBagItElement(bagInfo, cancellationToken);

        var tagManifest = await bag.LoadBagItElement<BagItTagManifest>(CancellationToken.None);
        tagManifest.AddOrUpdateItem(new(BagItInfo.FileName, SHA256.HashData(bagInfoContents)));
        await bag.StoreBagItElement(tagManifest, CancellationToken.None);
    }

    private async ValueTask<IAsyncDisposable> TryAcquireWriteLockAsync(
        DatasetVersion datasetVersion,
        CancellationToken cancellationToken) =>
        await _lockProvider.TryAcquireWriteLockAsync(
            "datasetVersion:" + datasetVersion.Identifier + "-" + datasetVersion.Version,
            cancellationToken) ?? throw new ConflictException();
}
