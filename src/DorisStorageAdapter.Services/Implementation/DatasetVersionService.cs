using ByteSizeLib;
using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DorisStorageAdapter.Services.Implementation.BagIt;
using DorisStorageAdapter.Services.Implementation.BagIt.Fetch;
using DorisStorageAdapter.Services.Implementation.BagIt.Info;
using DorisStorageAdapter.Services.Implementation.BagIt.Manifest;
using DorisStorageAdapter.Services.Implementation.Lock;
using Microsoft.Extensions.Options;
using DorisStorageAdapter.Services.Implementation.Configuration;

namespace DorisStorageAdapter.Services.Implementation;

internal sealed class DatasetVersionService(
    ILockService lockService,
    MetadataService metadataService,
    IOptions<StorageConfiguration> storageConfiguration) : IDatasetVersionService
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
        long octetCount = 0;
        bool payloadFileFound = false;
        await foreach (var file in metadataService.ListPayloadFiles(datasetVersion, null, cancellationToken))
        {
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
