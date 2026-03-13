namespace DorisStorageAdapter.Services.Implementation.BagIt.Manifest;

internal sealed class BagItTagManifest : BagItManifest<BagItTagManifest>, IBagItElement<BagItTagManifest>
{
    public static BagItTagManifest CreateEmpty() => new();

    public static string FileName => "tagmanifest-sha256.txt";
}
