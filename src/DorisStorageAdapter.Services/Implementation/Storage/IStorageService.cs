using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Storage;

internal interface IStorageService
{
    Task<StorageFileBaseMetadata> StoreAsync(string filePath, Stream data, long size, string? contentType, CancellationToken cancellationToken);

    Task DeleteAsync(string filePath, CancellationToken cancellationToken);

    Task<StorageFileMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken);

    Task<StorageFileData?> GetDataAsync(string filePath, StorageByteRange? byteRange, CancellationToken cancellationToken);

    IAsyncEnumerable<StorageFileMetadata> ListAsync(string path, CancellationToken cancellationToken);
}