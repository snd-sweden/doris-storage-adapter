using DorisStorageAdapter.Services.Contract.Models;
using System;
using System.Linq;

namespace DorisStorageAdapter.Services.Implementation;

internal static class Paths
{
    private static readonly string[] legacyPrefixes = ["ecds", "ext", "snd"];

    public static string GetPayloadPath(FileType? type) =>
       "data/" + (type == null ? "" : type.ToString() + '/');

    public static string GetDatasetPath(DatasetVersion datasetVersion)
    {
        // If dataset identifier begins with one of the legacy prefixes,
        // use that prefix as a base path.
        // Otherwise, use the string left of the first '-' as base path, or 
        // empty string if there is no '-' in the dataset identifier.

        string basePath = legacyPrefixes.FirstOrDefault(p =>
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

    public static string GetVersionPath(DatasetVersion datasetVersion) =>
        datasetVersion.Identifier + '-' + datasetVersion.Version;

    public static string GetDatasetVersionPath(DatasetVersion datasetVersion) =>
        GetDatasetPath(datasetVersion) + GetVersionPath(datasetVersion) + '/';

    public static (string VersionPath, string FilePath) ParseFetchUrl(string url)
    {
        string path = Uri.UnescapeDataString(url[3..]);
        int index = path.IndexOf('/', StringComparison.Ordinal) + 1;

        return (path[index..], path[..index]);
    }
}
