namespace DorisStorageAdapter.Services.Contract.Models;

public sealed record StorageContext(
    string? PartitionId = null)
{
    public static readonly StorageContext None = new();
}
