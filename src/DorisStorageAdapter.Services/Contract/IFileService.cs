using DorisStorageAdapter.Services.Contract.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Contract;

public interface IFileService
{
    Task<FileMetadata> StoreAsync(
        DatasetVersion datasetVersion,
        string filePath,
        Stream data,
        long size,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        DatasetVersion datasetVersion,
        string filePath,
        CancellationToken cancellationToken);

    Task ImportAsync(
        DatasetVersion datasetVersion,
        string fromVersion,
        CancellationToken cancellationToken);

    Task<FileData?> GetDataAsync(
        DatasetVersion datasetVersion,
        string filePath,
        FileAccessScope scope,
        ByteRange? byteRange,
        CancellationToken cancellationToken);

    Task<FileMetadata?> GetMetaDataAsync(
       DatasetVersion datasetVersion,
       string filePath,
       FileAccessScope scope,
       CancellationToken cancellationToken);

    IAsyncEnumerable<FileMetadata> ListAsync(
        DatasetVersion datasetVersion,
        CancellationToken cancellationToken);

    Task<bool> TryWriteDataAsZipAsync(
        DatasetVersion datasetVersion,
        string[] paths,
        Stream stream,
        FileAccessScope scope,
        CancellationToken cancellationToken);
}
