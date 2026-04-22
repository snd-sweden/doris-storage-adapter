using DorisStorageAdapter.BagIt.Fetch;
using DorisStorageAdapter.BagIt.Manifest;
using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.Services.Bags;
using DorisStorageAdapter.Services.Implementation.Services.Locking;
using DorisStorageAdapter.Services.Implementation.Services.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services;

internal sealed class CheckService(
    DatasetVersionLocks datasetVersionLocks,
    BagContextFactory bagContextFactory) : ICheckService
{
    private readonly DatasetVersionLocks _datasetVersionLocks = datasetVersionLocks;
    private readonly BagContextFactory _bagContextFactory = bagContextFactory;

    public async Task<IReadOnlyList<ErrorItem>> CheckConsistencyAsync(
        DatasetVersion datasetVersion, 
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        DatasetVersionValidator.ThrowIfInvalid(datasetVersion);

        await using var datasetVersionLock = await _datasetVersionLocks
            .AcquireExclusiveLockOrThrowAsync(datasetVersion, cancellationToken);

        var bagContext = _bagContextFactory.Create(datasetVersion);

        var payloadFilePaths = await bagContext
            .ListPayloadFilesAsync(cancellationToken)
            .Select(f => f.Path)
            .ToHashSetAsync(cancellationToken: cancellationToken);

        var fetch = await bagContext.LoadBagItElementAsync<BagItFetch>(cancellationToken);
        var payloadManifest = await bagContext.LoadBagItElementAsync<BagItPayloadManifest>(cancellationToken);

        return await BagConsistencyChecker.CheckAsync(
            _bagContextFactory,
            bagContext,
            payloadFilePaths,
            fetch,
            payloadManifest,
            cancellationToken);
    }
}
