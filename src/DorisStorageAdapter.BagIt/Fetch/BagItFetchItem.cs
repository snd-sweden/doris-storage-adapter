namespace DorisStorageAdapter.BagIt.Fetch;

public sealed record BagItFetchItem(
    string FilePath,
    long? Length,
#pragma warning disable CA1054 // URI parameters should not be strings
#pragma warning disable CA1056 // URI properties should not be strings
    string Url);
#pragma warning restore CA1054
#pragma warning restore CA1056
