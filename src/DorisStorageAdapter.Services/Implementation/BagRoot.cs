using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.BagIt;
using DorisStorageAdapter.Services.Implementation.BagIt.Fetch;
using DorisStorageAdapter.Services.Implementation.Storage;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation;

internal sealed class BagRoot(string rootPath, IStorageService storageService)
{
    private readonly IStorageService _storageService = storageService;

    public string RootPath { get; } = rootPath;

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
        string filePath = RootPath + T.FileName;

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
        FileType? type,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var file in _storageService.List(
            RootPath + Paths.GetPayloadPath(type),
            cancellationToken))
        {
            yield return file with { Path = file.Path[RootPath.Length..] };
        }
    }

    public async Task<bool> HasBeenPublished(CancellationToken cancellationToken) =>
        await _storageService.GetMetadata(RootPath + BagItDeclaration.FileName, cancellationToken) != null;

    private Task<StorageFileData?> GetBagItElementFileData<T>(CancellationToken cancellationToken)
       where T : IBagItElement<T> =>
       _storageService.GetData(
           RootPath + T.FileName, null, cancellationToken);
}
