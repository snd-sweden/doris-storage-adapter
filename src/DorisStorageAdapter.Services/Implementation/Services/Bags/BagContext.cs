using DorisStorageAdapter.BagIt;
using DorisStorageAdapter.BagIt.Fetch;
using DorisStorageAdapter.BagIt.Info;
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

    public const ChecksumAlgorithm ChecksumAlgorithm = ChecksumAlgorithm.Sha256;

    public BagContext(string storagePath, IStorageProvider storageProvider)
    {
        StoragePath = storagePath;
        _storageProvider = storageProvider;

        int index = storagePath.TrimEnd('/').LastIndexOf('/') + 1;
        _groupStoragePath = storagePath[..index];
        _versionPath = storagePath[index..];
    }

    public string StoragePath { get; }

    public async Task<BagItFetch> LoadFetchAsync(CancellationToken cancellationToken)
    {
        var (result, _) = await LoadBagItElementAsync(
            BagItFetch.FileName,
            BagItFetch.ParseAsync,
            ThrowIfInvalidFetch,
            false,
            cancellationToken);

        return result ?? new();
    }

    public async Task<BagItInfo> LoadInfoAsync(CancellationToken cancellationToken)
    {
        var (result, _) = await LoadBagItElementAsync(
            BagItInfo.FileName,
            BagItInfo.ParseAsync,
            null,
            false,
            cancellationToken);

        return result ?? new();
    }

    public async Task<BagItPayloadManifest> LoadPayloadManifestAsync(CancellationToken cancellationToken)
    {
        var (result, _) = await LoadBagItElementAsync(
           BagItPayloadManifest.GetFileName(ChecksumAlgorithm),
           ParsePayloadManifestAsync,
           ThrowIfInvalidPayloadManifest,
           false,
           cancellationToken);

        return result ?? new(ChecksumAlgorithm);
    }

    public async Task<BagItTagManifest> LoadTagManifestAsync(CancellationToken cancellationToken)
    {
        var (result, _) = await LoadBagItElementAsync(
            BagItTagManifest.GetFileName(ChecksumAlgorithm),
            ParseTagManifestAsync,
            ThrowIfInvalidTagManifest,
            false,
            cancellationToken);

        return result ?? new(ChecksumAlgorithm);
    }

    private async Task<(T? Element, Checksum? Checksum)> LoadBagItElementAsync<T>(
        string fileName,
        Func<Stream, CancellationToken, Task<T>> parser,
        Action<T>? validate,
        bool withChecksum,
        CancellationToken cancellationToken)
        where T : IBagItElement
    {
        var fileData = await GetFileDataAsync(fileName, null, cancellationToken);

        if (fileData == null)
        {
            return (default, null);
        }

        await using var baseStream = fileData.Stream;

        await using CountedHashStream? hashStream =
            withChecksum ? new CountedHashStream(baseStream) : null;

        var stream = hashStream ?? baseStream;

        T element;
        try
        {
            element = await parser(stream, cancellationToken);     
        }
        catch (BagItParseException e)
        {
            throw new DatasetIntegrityException(
                $"Error parsing BagIt file {fileName}.",
                [new(e.Message, fileName)]);
        }

        validate?.Invoke(element);

        return hashStream == null
            ? (element, null)
            : (element, new Checksum(ChecksumAlgorithm, hashStream.GetHash()));
    }

    private static Task<BagItPayloadManifest> ParsePayloadManifestAsync(
        Stream stream,
        CancellationToken cancellationToken) =>
        BagItPayloadManifest.ParseAsync(stream, ChecksumAlgorithm, cancellationToken);

    private static Task<BagItTagManifest> ParseTagManifestAsync(
        Stream stream,
        CancellationToken cancellationToken) =>
        BagItTagManifest.ParseAsync(stream, ChecksumAlgorithm, cancellationToken);


    public Task<(BagItFetch Fetch, Checksum Checksum)?> LoadFetchWithChecksumAsync(
        CancellationToken cancellationToken) =>
        LoadBagItElementWithChecksumAsync(
            BagItFetch.FileName,
            BagItFetch.ParseAsync,
            ThrowIfInvalidFetch,
            cancellationToken);

    public Task<(BagItInfo Info, Checksum Checksum)?> LoadInfohWithChecksumAsync(
        CancellationToken cancellationToken) =>
        LoadBagItElementWithChecksumAsync(
            BagItInfo.FileName,
            BagItInfo.ParseAsync,
            null,
            cancellationToken);

    public Task<(BagItPayloadManifest Manifest, Checksum Checksum)?> LoadPayloadManifestWithChecksumAsync(
        CancellationToken cancellationToken) =>
        LoadBagItElementWithChecksumAsync(
            BagItPayloadManifest.GetFileName(ChecksumAlgorithm),
            ParsePayloadManifestAsync,
            ThrowIfInvalidPayloadManifest,
            cancellationToken);

    private async Task<(T Element, Checksum Checksum)?> LoadBagItElementWithChecksumAsync<T>(
        string fileName,
        Func<Stream, CancellationToken, Task<T>> parser,
        Action<T>? validate,
        CancellationToken cancellationToken)
        where T : IBagItElement
    {
        var (element, checksum) = await LoadBagItElementAsync(
            fileName,
            parser,
            validate,
            withChecksum: true,
            cancellationToken);

        return element == null
            ? null
            : (element, checksum ?? throw new InvalidOperationException(
                "Checksum was not calculated."));
    }

    public Task<byte[]> StoreFetchAsync(
        BagItFetch fetch, CancellationToken cancellationToken) =>
        StoreBagItElementAsync(fetch, BagItFetch.FileName, cancellationToken);

    public Task<byte[]> StoreInfoAsync(
        BagItInfo info, CancellationToken cancellationToken) =>
        StoreBagItElementAsync(info, BagItInfo.FileName, cancellationToken);

    public Task<byte[]> StoreBagItDeclarationAsync(
        CancellationToken cancellationToken) =>
        StoreBagItElementAsync(BagItDeclaration.Instance, BagItDeclaration.FileName, cancellationToken);

    public Task<byte[]> StorePayloadManifestAsync(
        BagItPayloadManifest manifest, CancellationToken cancellationToken) =>
        StoreBagItElementAsync(manifest, BagItPayloadManifest.GetFileName(ChecksumAlgorithm), cancellationToken);

    public Task<byte[]> StoreTagManifestAsync(
       BagItTagManifest manifest, CancellationToken cancellationToken) =>
       StoreBagItElementAsync(manifest, BagItTagManifest.GetFileName(ChecksumAlgorithm), cancellationToken);


    private async Task<byte[]> StoreBagItElementAsync<T>(
        T element, string fileName, CancellationToken cancellationToken)
        where T : IBagItElement
    {
        string storagePath = ToStoragePath(fileName);

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
        ListFilesAsync(BagPathLayout.PayloadRootPath, true, cancellationToken);

    public IAsyncEnumerable<StorageFileMetadata> ListFilesAsync(
        string pathInBag,
        bool recursive,
        CancellationToken cancellationToken) =>
        _storageProvider
            .ListAsync(ToStoragePath(pathInBag), recursive, cancellationToken)
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
            ThrowIfInvalidFilePath(BagItPayloadManifest.GetFileName(ChecksumAlgorithm), item.FilePath, true);
        }
    }

    private static void ThrowIfInvalidTagManifest(BagItTagManifest manifest)
    {
        foreach (var item in manifest.Items)
        {
            ThrowIfInvalidFilePath(BagItTagManifest.GetFileName(ChecksumAlgorithm), item.FilePath, false);
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
