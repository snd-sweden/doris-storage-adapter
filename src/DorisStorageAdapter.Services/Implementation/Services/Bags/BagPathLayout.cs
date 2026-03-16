using DorisStorageAdapter.Services.Contract.Models;
using System;

namespace DorisStorageAdapter.Services.Implementation.Services.Bags;

internal static class BagPathLayout
{
    public const string PayloadRootPath = "data/";

    private const string DataRootPath = "data/data/";
    private const string DocumentationRootPath = "data/documentation/";

    public static string ToPathInBag(FileType type, string filePath) =>
         type switch
         {
             FileType.data => DataRootPath + filePath,
             FileType.documentation => DocumentationRootPath + filePath,
             _ => throw new ArgumentOutOfRangeException(nameof(type))
         };

    public static (FileType Type, string FilePath) FromPathInBag(string pathInBag)
    {
        if (pathInBag.StartsWith(DataRootPath, StringComparison.Ordinal))
        {
            return (FileType.data, pathInBag[DataRootPath.Length..]);
        }

        if (pathInBag.StartsWith(DocumentationRootPath, StringComparison.Ordinal))
        {
            return (FileType.documentation, pathInBag[DocumentationRootPath.Length..]);
        }

        throw new ArgumentException("Not a valid bag path.", nameof(pathInBag));
    }
}
