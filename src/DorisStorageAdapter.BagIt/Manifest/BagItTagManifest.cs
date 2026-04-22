using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.BagIt.Manifest;

public sealed class BagItTagManifest : BagItManifest<BagItTagManifest>, IBagItElement<BagItTagManifest>
{
    public static BagItTagManifest CreateEmpty() => new();

    public static string FileName => "tagmanifest-sha256.txt";

    public static Task<BagItTagManifest> ParseAsync(
        Stream stream, CancellationToken cancellationToken) =>
        ParseCoreAsync(stream, cancellationToken);
}
