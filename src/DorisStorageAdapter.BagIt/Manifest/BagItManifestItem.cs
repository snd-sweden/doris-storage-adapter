namespace DorisStorageAdapter.BagIt.Manifest;

public sealed record BagItManifestItem(
    string FilePath,
    Checksum Checksum);