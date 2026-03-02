using DorisStorageAdapter.Services.Implementation.BagIt;
using DorisStorageAdapter.Services.Implementation.Storage;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation;

internal sealed class Bag(string path, IStorageService storageService)
{
    private readonly IStorageService _storageService = storageService;

    // Härleda dataset-path? Eller är det utanför scope så att säga?

    public string Path { get; } = path;

    public string BagGroupPath { get; } = path[..(path.TrimEnd('/').LastIndexOf('/') + 1)];

    public async Task<T> LoadBagItElement<T>(CancellationToken cancellationToken)
        where T : class, IBagItElement<T>, new()
    {
        var fileData = await GetBagItElementFileData<T>(cancellationToken);

        if (fileData == null)
        {
            return new();
        }

        using (fileData.Stream)
        {
            return await T.Parse(fileData.Stream, cancellationToken);
        }
    }

    public async Task<(T BagItElement, byte[] Checksum)?> LoadBagItElementWithChecksum<T>(
        CancellationToken cancellationToken)
        where T : IBagItElement<T>
    {
        var fileData = await GetBagItElementFileData<T>(cancellationToken);

        if (fileData == null)
        {
            return null;
        }

        using var hashStream = new CountedHashStream(fileData.Stream);
        return (await T.Parse(hashStream, cancellationToken), hashStream.GetHash());
    }

    public async Task<byte[]> StoreBagItElement<T>(
        T element, CancellationToken cancellationToken)
        where T : IBagItElement<T>
    {
        string filePath = Path + T.FileName;

        if (element.HasValues())
        {
            var bytes = element.Serialize();

            using var stream = new MemoryStream(bytes);
            await _storageService.Store(
                filePath,
                stream,
                stream.Length,
                "text/plain",
                cancellationToken);

            return bytes;
        }

        await _storageService.Delete(filePath, cancellationToken);
        return [];
    }

    public async IAsyncEnumerable<StorageFileMetadata> ListPayloadFiles(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var file in _storageService.List(Path + Paths.GetPayloadPath(null), cancellationToken))
        {
            yield return file with { Path = file.Path[Path.Length..] };
        }
    }

    public async Task<bool> HasBeenPublished(CancellationToken cancellationToken) =>
        await _storageService.GetMetadata(Path + BagItDeclaration.FileName, cancellationToken) != null;

    private Task<StorageFileData?> GetBagItElementFileData<T>(CancellationToken cancellationToken)
       where T : IBagItElement<T> =>
       _storageService.GetData(
           Path + T.FileName, null, cancellationToken);
}
