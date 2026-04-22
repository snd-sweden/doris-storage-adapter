namespace DorisStorageAdapter.BagIt.Info;

public sealed record PayloadOxum(
    long OctetCount,
    long StreamCount);