using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.BagIt;
using DorisStorageAdapter.Services.Implementation.Storage;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services.Bags;

internal sealed class BagContext(string storagePath, IStorageService storageService)
{
    private readonly IStorageService _storageService = storageService;

    public string StoragePath { get; } = storagePath;

    // Är det här smart, eller göra på annat sätt?
    // Ange direkt i konstruktor tex?
    public string GroupStoragePath { get; } = storagePath[..(storagePath.TrimEnd('/').LastIndexOf('/') + 1)];

    public async Task<T> LoadBagItElementAsync<T>(CancellationToken cancellationToken)
        where T : IBagItElement<T>
    {
        var fileData = await GetFileDataAsync(T.FileName, null, cancellationToken);

        if (fileData == null)
        {
            return T.CreateEmpty();
        }

        await using (fileData.Stream)
        {
            return await T.ParseAsync(fileData.Stream, cancellationToken);
        }
    }

    public async Task<(T BagItElement, byte[] Checksum)?> LoadBagItElementWithChecksumAsync<T>(
        CancellationToken cancellationToken)
        where T : IBagItElement<T>
    {
        var fileData = await GetFileDataAsync(T.FileName, null, cancellationToken);

        if (fileData == null)
        {
            return null;
        }

        await using var hashStream = new CountedHashStream(fileData.Stream);
        return (await T.ParseAsync(hashStream, cancellationToken), hashStream.GetHash());
    }

    public async Task<byte[]> StoreBagItElementAsync<T>(
        T element, CancellationToken cancellationToken)
        where T : IBagItElement<T>
    {
        string storagePath = ToStoragePath(T.FileName);

        if (element.HasValues())
        {
            var bytes = element.Serialize();

            using var stream = new MemoryStream(bytes);
            await _storageService.StoreAsync(
                storagePath,
                stream,
                stream.Length,
                "text/plain",
                cancellationToken);

            return bytes;
        }

        await _storageService.DeleteAsync(storagePath, cancellationToken);
        return [];
    }

    public async IAsyncEnumerable<StorageFileMetadata> ListPayloadFilesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var file in _storageService.ListAsync(ToStoragePath(Paths.GetPayloadPath(null)), cancellationToken))
        {
            // TODO borde här egentligen returnera en annan typ, för att indikera att det är pathInBag
            // och inte storage path?
            yield return file with { Path = FromStoragePath(file.Path) };
        }
    }

    public Task<StorageFileBaseMetadata> StoreFileAsync(
        string path,
        Stream data,
        long size,
        string? contentType,
        CancellationToken cancellationToken) =>
        _storageService.StoreAsync(
               ToStoragePath(path),
               data,
               size,
               contentType,
               cancellationToken);

    public Task DeleteFileAsync(
        string path,
        CancellationToken cancellationToken) =>
        _storageService.DeleteAsync(ToStoragePath(path), cancellationToken);

    public Task<StorageFileData?> GetFileDataAsync(
        string pathInBag,
        ByteRange? byteRange,
        CancellationToken cancellationToken) =>
        _storageService.GetDataAsync(
            ToStoragePath(pathInBag),
            byteRange == null ? null : new(byteRange.From, byteRange.To),
            cancellationToken);

    public async Task<StorageFileMetadata?> GetFileMetadataAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var result = await _storageService.GetMetadataAsync(
            ToStoragePath(path),
            cancellationToken);

        if (result == null)
        {
            return null;
        }

        return result with { Path = FromStoragePath(result.Path) };
    }

    public async Task<bool> HasBeenPublishedAsync(CancellationToken cancellationToken) =>
        await GetFileMetadataAsync(BagItDeclaration.FileName, cancellationToken) != null;

    private string ToStoragePath(string path) =>
        StoragePath + path;

    private string FromStoragePath(string path) =>
        path[StoragePath.Length..];
}
