using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.BagIt;
using DorisStorageAdapter.Services.Implementation.BagIt.Fetch;
using DorisStorageAdapter.Services.Implementation.BagIt.Manifest;
using DorisStorageAdapter.Services.Implementation.Lock;
using DorisStorageAdapter.Services.Implementation.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation;

internal class DatasetVersionRepository(IStorageService storageService, ILockService lockService, MetadataService metadataService) : IDatasetVersionRepository
{
    public async Task DeleteFile(DatasetVersion datasetVersion, FileType type, string relativePath, CancellationToken cancellationToken)
    {
        bool lockSuccessful = false;
        await lockService.TryLockDatasetVersionShared(datasetVersion, async () =>
        {
            string fullFilePath = Paths.GetFullFilePath(datasetVersion, relativePath);

            lockSuccessful = await lockService.TryLockPath(fullFilePath, async () =>
            {
                await storageService.Delete(fullFilePath, cancellationToken);

                // Do not cancel the operation from this point on,
                // since the file has been successfully deleted.

                await RemoveItemFromPayloadManifest(datasetVersion, relativePath, CancellationToken.None);
                await RemoveItemFromFetch(datasetVersion, relativePath, CancellationToken.None);
            },
            cancellationToken);
        },
        cancellationToken);

        if (!lockSuccessful)
        {
            throw new ConflictException();
        }
    }

    public Task<FileData?> GetFileData(DatasetVersion datasetVersion, FileType type, string relativePath, bool isHeadRequest, ByteRange? byteRange, bool allowDraft, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task ImportFromVersion(DatasetVersion datasetVersion, string fromVersion, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<FileMetadata> ListFiles(DatasetVersion datasetVersion, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<FileMetadata> StoreFile(DatasetVersion datasetVersion, FileType type, string relativePath, Stream data, long size, string? contentType, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task WriteFilesAsZip(DatasetVersion datasetVersion, string[] relativePaths, Stream output, bool allowDraft, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    private Task AddOrUpdatePayloadManifestItem(
     DatasetVersion datasetVersion,
     BagItManifestItem item,
     CancellationToken cancellationToken) =>
     LockAndUpdateBagItElement<BagItPayloadManifest>(
         datasetVersion, manifest => manifest.AddOrUpdateItem(item), cancellationToken);

    private Task RemoveItemFromPayloadManifest(
        DatasetVersion datasetVersion,
        string filePath,
        CancellationToken cancellationToken) =>
        LockAndUpdateBagItElement<BagItPayloadManifest>(
            datasetVersion, manifest => manifest.RemoveItem(filePath), cancellationToken);

    private Task RemoveItemFromFetch(
        DatasetVersion datasetVersion,
        string filePath,
        CancellationToken cancellationToken) =>
        LockAndUpdateBagItElement<BagItFetch>(datasetVersion, fetch => fetch.RemoveItem(filePath), cancellationToken);

    private async Task LockAndUpdateBagItElement<T>(
        DatasetVersion datasetVersion,
        Func<T, bool> action,
        CancellationToken cancellationToken)
        where T : class, IBagItElement<T>, new()
    {
        // This method assumes that there is no exlusive lock on datasetVersion

        using (await lockService.LockPath(Paths.GetFullFilePath(datasetVersion, T.FileName), cancellationToken))
        {
            var element = await metadataService.LoadBagItElement<T>(datasetVersion, cancellationToken);

            if (action(element))
            {
                await metadataService.StoreBagItElement(datasetVersion, element, cancellationToken);
            }
        }
    }
}
