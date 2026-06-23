using ByteSizeLib;
using DorisStorageAdapter.BagIt;
using DorisStorageAdapter.BagIt.Fetch;
using DorisStorageAdapter.BagIt.Info;
using DorisStorageAdapter.BagIt.Manifest;
using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.Configuration;
using DorisStorageAdapter.Services.Implementation.Services.Bags;
using DorisStorageAdapter.Services.Implementation.Services.Locking;
using DorisStorageAdapter.Services.Implementation.Services.Validation;
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
    DatasetVersionValidator datasetVersionValidator,
    DatasetVersionLocks datasetVersionLocks,
    BagContextFactory bagContextFactory,
    IOptions<SystemConfiguration> systemConfiguration) : IStatusService
{
    private readonly DatasetVersionValidator _datasetVersionValidator = datasetVersionValidator;
    private readonly DatasetVersionLocks _datasetVersionLocks = datasetVersionLocks;
    private readonly BagContextFactory _bagContextFactory = bagContextFactory;
    private readonly SystemConfiguration _systemConfiguration = systemConfiguration.Value;

    private static readonly Checksum _bagItSha256 =
        new(SHA256.HashData(BagItDeclaration.CreateEmpty().Serialize()));

    public async Task PublishAsync(
        DatasetVersion datasetVersion,
        AccessRight accessRight,
        string canonicalDoi,
        string doi,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        ArgumentException.ThrowIfNullOrEmpty(doi);
        _datasetVersionValidator.ThrowIfInvalid(datasetVersion);

        if (accessRight == AccessRight.Public &&
            _systemConfiguration.DatasetAccessMode != DatasetAccessMode.Open ||
            accessRight == AccessRight.NonPublic &&
            _systemConfiguration.DatasetAccessMode != DatasetAccessMode.Restricted)
        {
            throw new ValidationException([new(
                Target: nameof(accessRight),
                Message: "Requested access right does not match the system's configured dataset access mode.")]);
        }

        DoiValidator.ThrowIfInvalid(canonicalDoi);
        DoiValidator.ThrowIfInvalid(doi);

        await using var datasetVersionLock = await _datasetVersionLocks
            .AcquireExclusiveLockOrThrowAsync(datasetVersion, cancellationToken);

        var bagContext = _bagContextFactory.Create(datasetVersion);

        if (await bagContext.HasBeenPublishedAsync(cancellationToken))
        {
            // Already published, nothing to do.
            return;
        }

        var fetch = await bagContext.LoadBagItElementWithChecksumAsync<BagItFetch>(cancellationToken);

        var payloadFilePaths = new HashSet<string>(StringComparer.Ordinal);
        long octetCount = 0;

        await foreach (var file in bagContext.ListPayloadFilesAsync(cancellationToken))
        {
            payloadFilePaths.Add(file.Path);
            octetCount += file.Size;
        }

        if (payloadFilePaths.Count == 0 &&
            fetch?.BagItElement.HasValues() != true)
        {
            // No payload files found, nothing to do.
            return;
        }

        foreach (var item in fetch?.BagItElement.Items ?? [])
        {
            if (item.Length != null)
            {
                octetCount += item.Length.Value;
            }
        }

        var payloadManifest = await bagContext
            .LoadBagItElementWithChecksumAsync<BagItPayloadManifest>(cancellationToken);

        var errors = await BagConsistencyChecker.CheckAsync(
            _bagContextFactory,
            bagContext,
            payloadFilePaths,
            fetch?.BagItElement,
            payloadManifest?.BagItElement,
            cancellationToken);

        if (errors.Any())
        {
            throw new DatasetIntegrityException("Inconsistencies found in BagIt files.", errors);
        }

        var bagInfo = new BagItInfo
        {
            BaggingDate = DateOnly.FromDateTime(DateTime.UtcNow),
            BagGroupIdentifier = canonicalDoi,
            BagSize = ByteSize.FromBytes(octetCount).ToBinaryString(CultureInfo.InvariantCulture),
            ExternalIdentifier = [doi],
            InternalSenderIdentifier = [datasetVersion.Identifier + '-' + datasetVersion.Version],
            PayloadOxum = new(octetCount, payloadManifest?.BagItElement?.Items?.LongCount() ?? 0),
        };

        bagInfo.SetAccessRight(accessRight);
        bagInfo.SetDatasetVersionStatus(DatasetVersionStatus.Published);
        bagInfo.SetVersion(datasetVersion.Version);

        byte[] bagInfoContents = await bagContext.StoreBagItElementAsync(bagInfo, cancellationToken);

        // Add bagit.txt, bag-info.txt and manifest-sha256.txt to tagmanifest-sha256.txt.
        var tagManifest = await bagContext.LoadBagItElementAsync<BagItTagManifest>(CancellationToken.None);
        tagManifest.AddOrUpdateItem(new(BagItDeclaration.FileName, _bagItSha256));
        tagManifest.AddOrUpdateItem(new(BagItInfo.FileName, new(SHA256.HashData(bagInfoContents))));
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

    public async Task SetStatusAsync(
        DatasetVersion datasetVersion,
        DatasetVersionStatus status,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        _datasetVersionValidator.ThrowIfInvalid(datasetVersion);

        await using var datasetVersionLock = await _datasetVersionLocks
            .AcquireExclusiveLockOrThrowAsync(datasetVersion, cancellationToken);

        var bagContext = _bagContextFactory.Create(datasetVersion);

        if (!await bagContext.HasBeenPublishedAsync(cancellationToken))
        {
            // Not published, nothing to do.
            return;
        }

        var bagInfo = await bagContext.LoadBagItElementAsync<BagItInfo>(cancellationToken);
        var currentStatus = bagInfo.GetDatasetVersionStatus();

        if (currentStatus == null)
        {
            // No status in bag, abort.
            return;
        }

        if (currentStatus == status)
        {
            // Status is already correct, nothing to do.
            return;
        }

        bagInfo.SetDatasetVersionStatus(status);
        byte[] bagInfoContents = await bagContext.StoreBagItElementAsync(bagInfo, cancellationToken);

        var tagManifest = await bagContext.LoadBagItElementAsync<BagItTagManifest>(CancellationToken.None);
        tagManifest.AddOrUpdateItem(new(BagItInfo.FileName, new(SHA256.HashData(bagInfoContents))));
        await bagContext.StoreBagItElementAsync(tagManifest, CancellationToken.None);
    }
}
