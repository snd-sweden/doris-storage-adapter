using System;

namespace DorisStorageAdapter.BagIt.Manifest;

public static class ChecksumAlgorithmExtensions
{
    public static string ToBagItName(this ChecksumAlgorithm algorithm) =>
        algorithm switch
        {
            ChecksumAlgorithm.Sha256 => "sha256",
            ChecksumAlgorithm.Sha512 => "sha512",
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };
}