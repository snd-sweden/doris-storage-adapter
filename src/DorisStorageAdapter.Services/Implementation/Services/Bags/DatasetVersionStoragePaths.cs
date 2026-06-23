using DorisStorageAdapter.Services.Contract.Models;
using System;
using System.Linq;

namespace DorisStorageAdapter.Services.Implementation.Services.Bags;

internal static class DatasetVersionStoragePaths
{
    private static readonly string[] _legacyPrefixes = ["ecds", "ext", "snd"];

    private static string GetDatasetPath(DatasetVersion datasetVersion)
    {
        // If dataset identifier begins with one of the legacy prefixes,
        // use that prefix as a base path.
        // Otherwise, use the string left of the first '-' as base path, or 
        // empty string if there is no '-' in the dataset identifier.

        string basePath = _legacyPrefixes.FirstOrDefault(p =>
            datasetVersion.Identifier.StartsWith(p, StringComparison.Ordinal)) ?? "";

        if (string.IsNullOrEmpty(basePath))
        {
            int index = datasetVersion.Identifier.IndexOf('-', StringComparison.Ordinal);

            if (index > 0)
            {
                basePath = datasetVersion.Identifier[..index];
            }
        }

        if (!string.IsNullOrEmpty(basePath))
        {
            basePath += '/';
        }

        return basePath + datasetVersion.Identifier + '/';
    }

    public static string GetDatasetVersionPath(DatasetVersion datasetVersion) =>
        (datasetVersion.TenantId == null
            ? ""
            : datasetVersion.TenantId + '/') +
        GetDatasetPath(datasetVersion) + 
        datasetVersion.Identifier + '-' + 
        datasetVersion.Version + '/';
}
