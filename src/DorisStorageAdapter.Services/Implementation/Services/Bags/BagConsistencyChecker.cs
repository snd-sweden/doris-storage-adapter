using DorisStorageAdapter.BagIt.Fetch;
using DorisStorageAdapter.BagIt.Manifest;
using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Implementation.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services.Bags;

internal static class BagConsistencyChecker
{
    public static async Task<IReadOnlyList<ErrorItem>> CheckAsync(
        BagContextFactory bagContextFactory,
        BagContext bagContext,
        IReadOnlySet<string> payloadFilePaths,
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
            if (fetch == null)
            {
                return;
            }

            foreach (var reference in bagContext.GroupFetchReferences(fetch))
            {
                var referencedBagContext = bagContextFactory.Create(reference.ReferencedBagStoragePath);

                var referencedVersionManifest = await referencedBagContext
                    .LoadBagItElementAsync<BagItPayloadManifest>(cancellationToken);

                var referencedVersionFiles = new Dictionary<string, StorageFileMetadata>(StringComparer.Ordinal);
                await foreach (var file in referencedBagContext.ListPayloadFilesAsync(cancellationToken))
                {
                    referencedVersionFiles[file.Path] = file;
                }

                bool referencedVersionIsPublished =
                    await referencedBagContext.HasBeenPublishedAsync(cancellationToken);

                foreach (var r in reference.References)
                {
                    string target = $"Fetch file:{r.Item.FilePath}";

                    if (!referencedVersionIsPublished)
                    {
                        AddError(target, "Does not reference a published dataset version.");
                    }

                    if (r.Item.Length == null)
                    {
                        AddError(target, "Missing length.");
                    }

                    if (!referencedVersionFiles.TryGetValue(r.PathInBag, out var referencedFile))
                    {
                        AddError(target, "Referenced payload file not found.");
                    }
                    else if (
                        r.Item.Length != null &&
                        r.Item.Length != referencedFile.Size)
                    {
                        AddError(target, "Size does not match referenced payload file's size.");
                    }

                    if (payloadManifest == null ||
                        !payloadManifest.TryGetItem(r.Item.FilePath, out var itemThisManifest))
                    {
                        AddError(target, "Not found in payload manifest.");
                    }
                    else if (
                        referencedVersionManifest == null ||
                        !referencedVersionManifest.TryGetItem(r.PathInBag, out var itemPreviousManifest) ||
                        itemThisManifest.Checksum != itemPreviousManifest.Checksum)
                    {
                        AddError(target, "Payload manifest checksum does not match referenced file's payload manifest checksum.");
                    }
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

        async Task CheckUploadMarkers()
        {
            await foreach (var file in bagContext.ListFilesAsync("", false, cancellationToken))
            {
                if (file.Path.StartsWith(FileService.UploadMarkerFilePrefix, StringComparison.Ordinal))
                {
                    var fileData = await bagContext.GetFileDataAsync(file.Path, null, cancellationToken);

                    if (fileData != null)
                    {
                        await using var stream = fileData.Stream;
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        string errorFileName = await reader.ReadToEndAsync(cancellationToken);

                        AddError(
                            $"Payload directory:{errorFileName}",
                            $"Found marker file indicating unfinished upload ({file.Path}");
                    }
                }
            }
        }

        CheckPayloadFilePaths();
        await CheckFetchAsync();
        CheckPayloadManifest();
        await CheckUploadMarkers();

        return errors;
    }
}
