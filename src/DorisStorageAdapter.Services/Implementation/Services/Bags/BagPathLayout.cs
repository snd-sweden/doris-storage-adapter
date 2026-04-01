namespace DorisStorageAdapter.Services.Implementation.Services.Bags;

internal static class BagPathLayout
{
    public const string PayloadRootPath = "data/";

    public static string ToPathInBag(string filePath) =>
        PayloadRootPath + filePath;

    public static string FromPathInBag(string pathInBag) =>
        pathInBag[PayloadRootPath.Length..];
}
