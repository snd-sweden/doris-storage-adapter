using DorisStorageAdapter.BagIt.Fetch;
using DorisStorageAdapter.BagIt.Manifest;
using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.Configuration;
using DorisStorageAdapter.Services.Implementation.Services.Bags;
using DorisStorageAdapter.Services.Implementation.Storage;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services;

internal class DeduplicationService(
    IStorageProvider storageProvider,
    IOptions<SystemConfiguration> systemConfiguration,
    BagContextFactory bagContextFactory) : IDeduplicationService
{
    private readonly IStorageProvider _storageProvider = storageProvider;
    private readonly SystemConfiguration _systemConfiguration = systemConfiguration.Value;
    private readonly BagContextFactory _bagContextFactory = bagContextFactory;

    public async Task DeduplicateAsync(CancellationToken cancellationToken)
    {      
        var datasets = new Dictionary<string, List<DatasetVersion>>();

        await foreach (var file in _storageProvider.ListAsync("", true, cancellationToken))
        {
            if (file.Path.EndsWith(
                '/' + BagItPayloadManifest.FileName, StringComparison.Ordinal))
            {
                // Found a bag
                if (!TryParseDatasetVersion(file.Path, out var datasetVersion))
                {
                    continue;
                }

                if (!datasets.TryGetValue(datasetVersion.Identifier, out var list))
                {
                    list = [];
                    datasets[datasetVersion.Identifier] = list;
                }

                list.Add(datasetVersion);
            }
        }


        // Måste använda CancellationToken.None mer eller mindre överallt?

        foreach (string identifier in datasets.Keys)
        {
            var canonicalFiles = new Dictionary<Checksum, (DatasetVersion Version, string Path)>();
            var removedPayloadFiles = new Dictionary<string, string>();
  
            foreach (var version in datasets[identifier]
                .OrderBy(v => int.Parse(v.Version.Split('.')[0], CultureInfo.InvariantCulture))
                .ThenBy(v => v.Version.Contains('.', StringComparison.Ordinal)
                    ? int.Parse(v.Version.Split('.')[1], CultureInfo.InvariantCulture)
                    : 0)
            )
            {
                var bagContext = _bagContextFactory.Create(version);
                var fetch = await bagContext.LoadBagItElementAsync<BagItFetch>(cancellationToken);

                if (await bagContext.HasBeenPublishedAsync(cancellationToken))
                {
                    var manifest = await bagContext.LoadBagItElementAsync<BagItPayloadManifest>(cancellationToken);                 
                    var payloadFilesToRemove = new List<string>();
                    bool fetchUpdated = false;

                    foreach (var item in manifest.Items)
                    {
                        if (canonicalFiles.TryGetValue(item.Checksum, out var duplicate) &&
                            duplicate.Version != version)
                        {
                            var wantedContext = _bagContextFactory.Create(duplicate.Version);

                            if (fetch.TryGetItem(item.FilePath, out var fetchItem))
                            {
                                // No need to delete payload file.
                                // Check if fetch references correct file.

                                var reference = bagContext.ParseFetchReference(fetchItem);

                                if (reference.ReferencedBagStoragePath != wantedContext.StoragePath ||
                                    reference.PathInBag != duplicate.Path)
                                {
                                    fetch.AddOrUpdateItem(new(
                                        fetchItem.FilePath,
                                        fetchItem.Length,
                                        bagContext.CreateFetchUrl(wantedContext, duplicate.Path)));

                                    fetchUpdated = true;
                                }
                            }
                            else
                            {
                                // Not in fetch, add item.

                                // Borde plocka längd från första listningen istället, eller cachea
                                var fileMetadata = await wantedContext
                                    .GetFileMetadataAsync(duplicate.Path, cancellationToken);

                                var newFetchItem = new BagItFetchItem(
                                    item.FilePath,
                                    fileMetadata!.Size,
                                    bagContext.CreateFetchUrl(wantedContext, duplicate.Path));

                                fetch.AddOrUpdateItem(newFetchItem);

                                fetchUpdated = true;

                                payloadFilesToRemove.Add(item.FilePath);
                                removedPayloadFiles[bagContext.StoragePath + item.FilePath] =
                                    newFetchItem.Url;
                         
                            }
                        }
                        else
                        {
                            // Blir sist vinner här om flera filer med samma checksumma
                            canonicalFiles[item.Checksum] = (version, item.FilePath);
                        }
                    }

                    if (fetchUpdated)
                    {
                        await bagContext.StoreBagItElementAsync(fetch, cancellationToken);

                        foreach (string filePath in payloadFilesToRemove)
                        {
                            await bagContext.DeleteFileAsync(filePath, CancellationToken.None);
                        }
                    }
                }
                else
                {
                    // On draft version, rewrite fetch.txt if necessary.
                    bool fetchUpdated = false;

                    foreach (var item in fetch.Items)
                    {
                        var reference = bagContext.ParseFetchReference(item);

                        if (removedPayloadFiles.TryGetValue(
                            reference.ReferencedBagStoragePath + reference.PathInBag,
                            out var newUrl))
                        {
                            fetch.AddOrUpdateItem(new(
                                item.FilePath,
                                item.Length,
                                newUrl));

                            fetchUpdated = true;
                        }
                    }

                    if (fetchUpdated)
                    {
                        await bagContext.StoreBagItElementAsync(fetch, cancellationToken);
                    }

                }         
            }
        }
    }

    private bool TryParseDatasetVersion(
        string path,
        [NotNullWhen(true)] out DatasetVersion? datasetVersion)
    {
        datasetVersion = null;

        var segments = path.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);


        int indexDelta = _systemConfiguration.EnableTenancy
            ? 0
            : 1;

        if (segments.Length < 4 - indexDelta)
        {
            return false;
        }

        var tenantId = _systemConfiguration.EnableTenancy
            ? segments[0]
            : null;

        var identifier = segments[2 - indexDelta];
        var versionSegment = segments[3 - indexDelta];

        var prefix = identifier + "-";

        if (!versionSegment.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var version = versionSegment[prefix.Length..];

        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        datasetVersion = new DatasetVersion(
            Identifier: identifier,
            Version: version,
            TenantId: tenantId);

        return true;
    }
}
