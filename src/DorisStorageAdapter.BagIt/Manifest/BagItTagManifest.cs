using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.BagIt.Manifest;

public sealed class BagItTagManifest(ChecksumAlgorithm algorithm) 
    : BagItManifest<BagItTagManifest>(algorithm)
{
    public static string GetFileName(ChecksumAlgorithm algorithm) =>
       BuildFileName("tagmanifest", algorithm);

    public static Task<BagItTagManifest> ParseAsync(
        Stream stream,
        ChecksumAlgorithm algorithm,
        CancellationToken cancellationToken) =>
        ParseCoreAsync(stream, new(algorithm), cancellationToken);
}
