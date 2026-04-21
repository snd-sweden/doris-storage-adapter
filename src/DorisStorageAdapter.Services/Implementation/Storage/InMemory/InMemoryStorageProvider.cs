using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Storage.InMemory;

internal sealed class InMemoryStorageProvider(InMemoryStorage storage) : IStorageProvider
{
    private readonly InMemoryStorage _storage = storage;

    public async Task<StorageFileBaseMetadata> StoreAsync(
        string filePath,
        Stream data,
        long size,
        string? contentType,
        CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        await data.CopyToAsync(memoryStream, cancellationToken);
        var byteArray = memoryStream.ToArray();

        return _storage
            .AddOrUpdate(filePath, byteArray, contentType)
            .Metadata;
    }

    public Task DeleteAsync(
        string filePath, 
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _storage.Remove(filePath);
        return Task.CompletedTask;
    }

    public Task<StorageFileMetadata?> GetMetadataAsync(
        string filePath, 
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_storage.TryGet(filePath, out var file))
        {
            return Task.FromResult<StorageFileMetadata?>(file.Metadata);
        }

        return Task.FromResult<StorageFileMetadata?>(null);
    }

    public Task<StorageFileData?> GetDataAsync(
        string filePath, 
        StorageByteRange? byteRange, 
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_storage.TryGet(filePath, out var file))
        {
            Stream stream = new MemoryStream(file.Data);

            if (byteRange != null)
            {
                stream = StreamHelpers.CreateByteRangeStream(stream, byteRange);
            }

            return Task.FromResult<StorageFileData?>(new(
                ContentType: file.Metadata.ContentType,
                Size: file.Data.LongLength,
                Stream: stream,
                StreamLength: stream.Length));
        }

        return Task.FromResult<StorageFileData?>(null);
    }

    public IAsyncEnumerable<StorageFileMetadata> ListAsync(
        string path,
        bool recursive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _storage
            .ListFiles(path, recursive)
            .Select(f => f.Metadata)
            .ToAsyncEnumerable();
    }
}
