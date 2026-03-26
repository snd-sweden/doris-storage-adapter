using DorisStorageAdapter.Services.Contract.Models;
using System;
using System.Security.Cryptography;
using System.Text;

namespace DorisStorageAdapter.Services.Implementation.Services.Locking;

internal static class LockKeys
{
    public static string BagStructure(DatasetVersion datasetVersion) =>
        $"bag-structure:{datasetVersion.Identifier}:{datasetVersion.Version}";

    public static string DatasetVersion(DatasetVersion datasetVersion) =>
        $"dataset-version:{datasetVersion.Identifier}:{datasetVersion.Version}";

    public static string DatasetVersionFile(DatasetVersion datasetVersion, FileType type, string filePath) =>
        $"dataset-version-file:{datasetVersion.Identifier}:{datasetVersion.Version}:{type}:{Hash(filePath)}";

    public static string Storage => "storage";

    private static string Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(hash);
    }
}