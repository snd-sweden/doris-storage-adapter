using DorisStorageAdapter.BagIt;
using DorisStorageAdapter.BagIt.Fetch;
using DorisStorageAdapter.BagIt.Manifest;
using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.IO;
using DorisStorageAdapter.Services.Implementation.Services.Validation;
using DorisStorageAdapter.Services.Implementation.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Services.Bags;

internal sealed class BagContext
{
    private readonly IStorageProvider _storageProvider;
    private readonly string _groupStoragePath;
    private readonly string _versionPath;

    public BagContext(string storagePath, IStorageProvider storageProvider)
    {
        StoragePath = storagePath;
        _storageProvider = storageProvider;

        int index = storagePath.TrimEnd('/').LastIndexOf('/') + 1;
        _groupStoragePath = storagePath[..index];
        _versionPath = storagePath[index..];
    }

    public string StoragePath { get; }

    public async Task<T> LoadBagItElementAsync<T>(CancellationToken cancellationToken)
        where T : IBagItElement<T>
    {
        var fileData = await GetFileDataAsync(T.FileName, null, cancellationToken);

        if (fileData == null)
        {
            return T.CreateEmpty();
        }

        await using var stream = fileData.Stream;
        var result = await ParseBagItElementOrThrow<T>(stream, cancellationToken);

        ValidateBagItElementOrThrow(result);

        return result;
    }

    public async Task<(T BagItElement, Checksum Checksum)?> LoadBagItElementWithChecksumAsync<T>(
        CancellationToken cancellationToken)
        where T : IBagItElement<T>
    {
        var fileData = await GetFileDataAsync(T.FileName, null, cancellationToken);

        if (fileData == null)
        {
            return null;
        }

        await using var hashStream = new CountedHashStream(fileData.Stream);
        var result = await ParseBagItElementOrThrow<T>(hashStream, cancellationToken);

        ValidateBagItElementOrThrow(result);

        return (result, new(hashStream.GetHash()));
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
                cancellationToken);

            return bytes;
        }

        await _storageProvider.DeleteAsync(storagePath, cancellationToken);
        return [];
    }

    public IAsyncEnumerable<StorageFileMetadata> ListPayloadFilesAsync(
        CancellationToken cancellationToken) =>
        ListFilesAsync(BagPathLayout.PayloadRootPath, true, cancellationToken);

    public IAsyncEnumerable<StorageFileMetadata> ListFilesAsync(
        string pathInBag,
        bool recursive,
        CancellationToken cancellationToken) =>
        _storageProvider
            .ListAsync(ToStoragePath(pathInBag), recursive, cancellationToken)
            .Select(file => file with { Path = FromStoragePath(file.Path) });

    public Task StoreFileAsync(
        string path,
        Stream data,
        long size,
        CancellationToken cancellationToken) =>
        _storageProvider.StoreAsync(
            ToStoragePath(path),
            data,
            size,
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
        void Throw()
        {
            throw new DatasetIntegrityException($"Error validating {BagItFetch.FileName}.",
                [new($"Invalid URL.", $"{BagItFetch.FileName}:{item.FilePath}")]);
        }

        if (string.IsNullOrEmpty(item.Url))
        {
            Throw();
        }

        if (!item.Url.StartsWith("../", StringComparison.Ordinal))
        {
            Throw();
        }

        if (!Uri.TryCreate(item.Url, UriKind.Relative, out _))
        {
            Throw();
        }

        if (item.Url.Contains('?', StringComparison.Ordinal) ||
            item.Url.Contains('#', StringComparison.Ordinal))
        {
            Throw();
        }

        string decoded = Uri.UnescapeDataString(item.Url[3..]);

        if (!PathValidation.HasOnlyValidComponents(decoded))
        {
            Throw();
        }

        int index = decoded.IndexOf('/', StringComparison.Ordinal) + 1;

        if (index <= 1)
        {
            Throw();
        }

        string referencedVersionPath = decoded[..index];

        // Check that versionPath does not point to this version.
        if (referencedVersionPath == _versionPath)
        {
            Throw();
        }

        string pathInBag = decoded[index..];

        if (!pathInBag.StartsWith(BagPathLayout.PayloadRootPath, StringComparison.Ordinal))
        {
            // Does not reference a payload file (under data/).
            Throw();
        }

        return new(
            ReferencedBagStoragePath: _groupStoragePath + referencedVersionPath,
            PathInBag: pathInBag,
            Item: item);
    }

    public string CreateFetchUrl(BagContext otherBag, string pathInBag) =>
        "../" +
            UrlEncodePath(otherBag.StoragePath[_groupStoragePath.Length..]) +
            UrlEncodePath(pathInBag);

    private static async Task<T> ParseBagItElementOrThrow<T>(Stream stream, CancellationToken cancellationToken)
         where T : IBagItElement<T>
    {
        try
        {
            return await T.ParseAsync(stream, cancellationToken);
        }
        catch (BagItParseException e)
        {
            throw new DatasetIntegrityException(
                $"Error parsing BagIt file {T.FileName}.", 
                    [new(e.Message, T.FileName)]);
        }
    }

    private void ValidateBagItElementOrThrow<T>(T element)
        where T : IBagItElement<T>
    {
        switch (element)
        {
            case BagItPayloadManifest payloadManifest:
                ThrowIfInvalidPayloadManifest(payloadManifest);
                break;

            case BagItTagManifest tagManifest:
                ThrowIfInvalidTagManifest(tagManifest);
                break;

            case BagItFetch fetch:
                ThrowIfInvalidFetch(fetch);
                break;
        }
    }

    private static string UrlEncodePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private string ToStoragePath(string path) =>
        StoragePath + path;

    private string FromStoragePath(string path) =>
        path[StoragePath.Length..];

    private void ThrowIfInvalidFetch(BagItFetch fetch)
    {
        foreach (var item in fetch.Items)
        {
            ParseFetchReference(item); // Throws if invalid.

            ThrowIfInvalidFilePath(BagItFetch.FileName, item.FilePath, true);
        }
    }

    private static void ThrowIfInvalidPayloadManifest(BagItPayloadManifest manifest)
    {
        foreach (var item in manifest.Items)
        {
            ThrowIfInvalidFilePath(BagItPayloadManifest.FileName, item.FilePath, true);
        }
    }

    private static void ThrowIfInvalidTagManifest(BagItTagManifest manifest)
    {
        foreach (var item in manifest.Items)
        {
            ThrowIfInvalidFilePath(BagItTagManifest.FileName, item.FilePath, false);
        }
    }

    private static void ThrowIfInvalidFilePath(string bagItFileName, string filePath, bool shouldBePayload)
    {
        bool isPayload = filePath.StartsWith(BagPathLayout.PayloadRootPath, StringComparison.Ordinal);

        if (!PathValidation.HasOnlyValidComponents(filePath) ||
            shouldBePayload != isPayload)
        {
            throw new DatasetIntegrityException($"Error validating {bagItFileName}.",
                [new("Invalid file path.", $"{bagItFileName}:{filePath}")]);
        }
    }
}
