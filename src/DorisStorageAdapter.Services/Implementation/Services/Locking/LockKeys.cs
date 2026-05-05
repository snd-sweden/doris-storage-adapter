using DorisStorageAdapter.Services.Contract.Models;
using System;
using System.Security.Cryptography;
using System.Text;

namespace DorisStorageAdapter.Services.Implementation.Services.Locking;

internal static class LockKeys
{
    public static string BagStructure(DatasetVersion datasetVersion) =>
        $"bag-structure:{DatasetVersionKey(datasetVersion)}";

    public static string DatasetVersion(DatasetVersion datasetVersion) =>
        $"dataset-version:{DatasetVersionKey(datasetVersion)}";

    public static string DatasetVersionFile(DatasetVersion datasetVersion, string filePath) =>
        $"dataset-version-file:{DatasetVersionKey(datasetVersion)}:{Hash(filePath)}";

    public static string Storage(string name) => $"storage:{name}";

    private static string DatasetVersionKey(DatasetVersion datasetVersion) =>
        (datasetVersion.TenantId is null 
            ? "" : 
            datasetVersion.TenantId + ':'
        ) +
        $"{datasetVersion.Identifier}:{datasetVersion.Version}";

    private static string Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(hash);
    }
}