using DorisStorageAdapter.Services.Contract.Models;
using System;
using System.Linq;

namespace DorisStorageAdapter.Services.Implementation.Services.Bags;

internal static class Paths
{
    private static readonly string[] _legacyPrefixes = ["ecds", "ext", "snd"];

    // Används för att lägga till och strippa bort "data/"
    // och för att få ut typ från sökväg
    public static string GetPayloadPath(FileType? type) =>
        type switch
        {
            FileType.data => "data/data/",
            FileType.documentation => "data/documentation/",
            _ => "data/"
        };
       

    // Används endast internt
    // Eg. GetBagGroupStoragePath?
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
        GetDatasetPath(datasetVersion) + datasetVersion.Identifier + '-' + datasetVersion.Version + '/';

    public static string ToPathInBag(FileType type, string filePath) =>
        GetPayloadPath(type) + filePath;

    public static (FileType Type, string FilePath) FromPathInBag(string pathInBag)
    {
        if (pathInBag.StartsWith(GetPayloadPath(FileType.data), StringComparison.Ordinal))
        {
            return (FileType.data, pathInBag[GetPayloadPath(FileType.data).Length..]);
        }

        if (pathInBag.StartsWith(GetPayloadPath(FileType.documentation), StringComparison.Ordinal))
        {
            return (FileType.documentation, pathInBag[GetPayloadPath(FileType.documentation).Length..]);
        }

        throw new ArgumentException("Not a valid bag path.", nameof(pathInBag));
    }
}
