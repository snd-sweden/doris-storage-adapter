using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.BagIt;
using DorisStorageAdapter.Services.Implementation.BagIt.Fetch;
using DorisStorageAdapter.Services.Implementation.IO;
using DorisStorageAdapter.Services.Implementation.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services.Bags;

internal sealed class BagContext(string storagePath, IStorageProvider storageProvider)
{
    private readonly IStorageProvider _storageProvider = storageProvider;
    private readonly string _groupStoragePath = storagePath[..(storagePath.TrimEnd('/').LastIndexOf('/') + 1)];

    public string StoragePath { get; } = storagePath;

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
            await _storageProvider.StoreAsync(
                storagePath,
                stream,
                stream.Length,
                "text/plain",
                cancellationToken);

            return bytes;
        }

        await _storageProvider.DeleteAsync(storagePath, cancellationToken);
        return [];
    }

    public IAsyncEnumerable<StorageFileMetadata> ListPayloadFilesAsync(
        CancellationToken cancellationToken) =>
        _storageProvider
            .ListAsync(ToStoragePath(BagPathLayout.PayloadRootPath), cancellationToken)
            .Select(file => file with { Path = FromStoragePath(file.Path) });

    public Task<StorageFileBaseMetadata> StoreFileAsync(
        string path,
        Stream data,
        long size,
        string? contentType,
        CancellationToken cancellationToken) =>
        _storageProvider.StoreAsync(
            ToStoragePath(path),
            data,
            size,
            contentType,
            cancellationToken);

    public Task DeleteFileAsync(
        string path,
        CancellationToken cancellationToken) =>
        _storageProvider.DeleteAsync(ToStoragePath(path), cancellationToken);

    public Task<StorageFileData?> GetFileDataAsync(
        string pathInBag,
        ByteRange? byteRange,
        CancellationToken cancellationToken) =>
        _storageProvider.GetDataAsync(
            ToStoragePath(pathInBag),
            byteRange == null ? null : new(byteRange.From, byteRange.To),
            cancellationToken);

    public async Task<StorageFileMetadata?> GetFileMetadataAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var result = await _storageProvider.GetMetadataAsync(
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

    public IEnumerable<FetchReferenceGroup> GroupFetchReferences(BagItFetch fetch) =>
       fetch.Items
            .Select(ParseFetchReference)
            .GroupBy(f => f.ReferencedBagStoragePath, StringComparer.Ordinal)
            .Select(g => new FetchReferenceGroup(
                g.Key,
                [.. g]));

    public FetchReference ParseFetchReference(BagItFetchItem item)
    {
        string path = Uri.UnescapeDataString(item.Url[3..]);
        int index = path.IndexOf('/', StringComparison.Ordinal) + 1;
        string versionPath = path[..index];
        string pathInBag = path[index..];

        return new(
            ReferencedBagStoragePath: _groupStoragePath + versionPath, 
            PathInBag: pathInBag,
            Item: item);
    }

    public string CreateFetchUrl(BagContext otherBag, string pathInBag) =>
        "../" + 
        UrlEncodePath(otherBag.StoragePath[_groupStoragePath.Length..]) + 
        UrlEncodePath(pathInBag);

    private static string UrlEncodePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private string ToStoragePath(string path) =>
        StoragePath + path;

    private string FromStoragePath(string path) =>
        path[StoragePath.Length..];
}
