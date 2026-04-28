using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.BagIt.Manifest;

public sealed class BagItPayloadManifest(ChecksumAlgorithm algorithm) 
    : BagItManifest<BagItPayloadManifest>(algorithm)
{
    public static string GetFileName(ChecksumAlgorithm algorithm) =>
       BuildFileName("manifest", algorithm);

    public static Task<BagItPayloadManifest> ParseAsync(
        Stream stream,
        ChecksumAlgorithm algorithm,
        CancellationToken cancellationToken) =>
        ParseCoreAsync(stream, new(algorithm), cancellationToken);
}
