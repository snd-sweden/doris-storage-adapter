using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.BagIt.Manifest;

public sealed class BagItPayloadManifest : BagItManifest<BagItPayloadManifest>, IBagItElement<BagItPayloadManifest>
{
    public static BagItPayloadManifest CreateEmpty() => new();

    public static string FileName => "manifest-sha256.txt";

    public static Task<BagItPayloadManifest> ParseAsync(
        Stream stream, CancellationToken cancellationToken) =>
        ParseCoreAsync(stream, cancellationToken);
}
