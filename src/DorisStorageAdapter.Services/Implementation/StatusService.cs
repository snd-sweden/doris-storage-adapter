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
    ILockService lockService,
    MetadataService metadataService,
    IOptions<StorageConfiguration> storageConfiguration) : IStatusService
{
    private readonly ILockService lockService = lockService;
    private readonly MetadataService metadataService = metadataService;
    private readonly StorageConfiguration storageConfiguration = storageConfiguration.Value;

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
            !storageConfiguration.AllowPublicAccessRight)
        {
            throw new PublicAccessRightNotAllowedException();
        }

        await ExecuteWithLock(datasetVersion, async () =>
        {
            if (await metadataService.VersionHasBeenPublished(datasetVersion, cancellationToken))
            {
                throw new DatasetStatusException();
            }

            await PublishImpl(datasetVersion, accessRight, canonicalDoi, doi, cancellationToken);
        },
        cancellationToken);
    }

    private async Task PublishImpl(
        DatasetVersion datasetVersion,
        AccessRight accessRight,
        string canonicalDoi,
        string doi,
        CancellationToken cancellationToken)
    {
        var fetch = await metadataService.LoadBagItElementWithChecksum<BagItFetch>(datasetVersion, cancellationToken);

        var payloadFilePaths = new HashSet<string>();
        long octetCount = 0;
        bool payloadFileFound = false;
        await foreach (var file in metadataService.ListPayloadFiles(datasetVersion, null, cancellationToken))
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

        var payloadManifest = await metadataService
            .LoadBagItElementWithChecksum<BagItPayloadManifest>(datasetVersion, cancellationToken);

        var errors = await PrePublishValidate(
            datasetVersion,
            payloadFilePaths,
            fetch?.BagItElement,
            payloadManifest?.BagItElement,
            cancellationToken);

        if (errors.Any())
        {
            throw new ValidationException(string.Join(@"\\n", errors));
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

        byte[] bagInfoContents = await metadataService.StoreBagItElement(datasetVersion, bagInfo, cancellationToken);

        // Add bagit.txt, bag-info.txt and manifest-sha256.txt to tagmanifest-sha256.txt
        var tagManifest = await metadataService.LoadBagItElement<BagItTagManifest>(datasetVersion, CancellationToken.None);
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

        await metadataService.StoreBagItElement(datasetVersion, tagManifest, CancellationToken.None);
        await metadataService.StoreBagItElement(datasetVersion, BagItDeclaration.Instance, CancellationToken.None);
    }

    private async Task<IEnumerable<string>> PrePublishValidate(
        DatasetVersion datasetVersion,
        HashSet<string> payloadFilePaths,
        BagItFetch? fetch,
        BagItPayloadManifest? payloadManifest,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        void CheckPayloadFilePaths()
        {
            foreach (var filePath in payloadFilePaths)
            {
                if (fetch != null &&
                    fetch.Contains(filePath))
                {
                    errors.Add($"Payload directory:{filePath} - Found in fetch file.");
                }

                if (payloadManifest == null ||
                    !payloadManifest.Contains(filePath))
                {
                    errors.Add($"Payload directory:{filePath} - Not found in payload manifest.");
                }
            }
        }

        async Task CheckFetch()
        {
            static string DecodeUrlEncodedPath(string path) => Uri.UnescapeDataString(path);

            static string RemoveCommonPrefix(string a, string b)
            {
                int max = Math.Min(a.Length, b.Length);
                int i = 0;

                while (i < max && a[i] == b[i])
                {
                    i++;
                }

                return b[i..];
            }

            string versionPath = Paths.GetVersionPath(datasetVersion);
            Dictionary<string, StorageFileMetadata> files = new(StringComparer.Ordinal);
            BagItPayloadManifest? itemManifest = null;
            bool isPublished = false;
            DatasetVersion? previousVersion = null;

            foreach (var item in (fetch?.Items ?? []).OrderBy(i => i.Url, StringComparer.Ordinal))
            {
                string itemPath = DecodeUrlEncodedPath(item.Url[3..]);
                int slashIndex = itemPath.IndexOf('/', StringComparison.Ordinal);
                string itemPayloadPath = itemPath[(slashIndex + 1)..];

                var itemVersion = new DatasetVersion(
                    datasetVersion.Identifier,
                    RemoveCommonPrefix(versionPath, itemPath[..slashIndex]));

                if (itemVersion != previousVersion)
                {
                    itemManifest = await metadataService
                        .LoadBagItElement<BagItPayloadManifest>(itemVersion, cancellationToken);

                    files = [];
                    await foreach (var file in metadataService.ListPayloadFiles(itemVersion, null, cancellationToken))
                    {
                        files[file.Path] = file;
                    }

                    isPublished =
                        await metadataService.VersionHasBeenPublished(itemVersion, cancellationToken);

                    previousVersion = itemVersion;
                }

                if (!isPublished)
                {
                    errors.Add($"Fetch file:{item.FilePath} - Does not reference a published dataset version.");
                }

                if (item.Length == null)
                {
                    errors.Add($"Fetch file:{item.FilePath} - Missing length.");
                }

                if (!files.TryGetValue(item.FilePath, out var referencedFile))
                {
                    errors.Add($"Fetch file:{item.FilePath} - Referenced payload file not found.");
                }
                else if (
                    item.Length != null &&
                    item.Length != referencedFile.Size)
                {
                    errors.Add($"Fetch file:{item.FilePath} - Size does not match referenced payload file's size.");
                }

                if (payloadManifest == null ||
                    !payloadManifest.TryGetItem(item.FilePath, out var itemThisManifest))
                {
                    errors.Add($"Fetch file:{item.FilePath} - Not found in payload manifest.");
                }
                else if (
                    itemManifest == null ||
                    !itemManifest.TryGetItem(itemPayloadPath, out var itemPreviousManifest) ||
                    !itemThisManifest.Checksum.SequenceEqual(itemPreviousManifest.Checksum))
                {
                    errors.Add($"Fetch file:{item.FilePath} - Payload manifest checksum does not match referenced file's payload manifest checksum.");
                }
            }
        }

        void CheckPayloadManifest()
        {
            foreach (var item in payloadManifest?.Items ?? [])
            {
                if ((fetch == null ||
                    !fetch.Contains(item.FilePath))
                    && !payloadFilePaths.Contains(item.FilePath))
                {
                    errors.Add($"Payload manifest:{item.FilePath} - Not found in payload directory or fetch file.");
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

        await ExecuteWithLock(datasetVersion, async () =>
        {
            if (!await metadataService.VersionHasBeenPublished(datasetVersion, cancellationToken))
            {
                throw new DatasetStatusException();
            }

            await SetStatusImpl(datasetVersion, status, cancellationToken);
        },
        cancellationToken);
    }

    private async Task SetStatusImpl(
        DatasetVersion datasetVersion,
        DatasetVersionStatus status,
        CancellationToken cancellationToken)
    {
        var bagInfo = await metadataService.LoadBagItElement<BagItInfo>(datasetVersion, cancellationToken);

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
        byte[] bagInfoContents = await metadataService.StoreBagItElement(datasetVersion, bagInfo, cancellationToken);

        var tagManifest = await metadataService.LoadBagItElement<BagItTagManifest>(datasetVersion, CancellationToken.None);
        tagManifest.AddOrUpdateItem(new(BagItInfo.FileName, SHA256.HashData(bagInfoContents)));
        await metadataService.StoreBagItElement(datasetVersion, tagManifest, CancellationToken.None);
    }

    private async Task ExecuteWithLock(
        DatasetVersion datasetVersion,
        Func<Task> task,
        CancellationToken cancellationToken)
    {
        bool lockSuccessful = await lockService.TryLockDatasetVersionExclusive(datasetVersion, task, cancellationToken);

        if (!lockSuccessful)
        {
            throw new ConflictException();
        }
    }
}
