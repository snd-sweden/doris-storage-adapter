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
        string? contentType,
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
        bool isHeadRequest,
        ByteRange? byteRange,
        bool allowDraft,
        CancellationToken cancellationToken);

    IAsyncEnumerable<FileMetadata> ListAsync(
        DatasetVersion datasetVersion,
        CancellationToken cancellationToken);

    Task WriteDataAsZipAsync(
        DatasetVersion datasetVersion,
        string[] paths,
        Stream stream,
        bool allowDraft,
        CancellationToken cancellationToken);
}
