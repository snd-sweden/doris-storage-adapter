using DorisStorageAdapter.Services.Contract.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Contract;

public interface IFileService
{
    Task<FileMetadata> Store(
        DatasetVersion datasetVersion,
        FileType type,
        string filePath,
        Stream data,
        long size,
        string? contentType,
        CancellationToken cancellationToken);

    Task Delete(
        DatasetVersion datasetVersion,
        FileType type,
        string filePath,
        CancellationToken cancellationToken);

    Task Import(
        DatasetVersion datasetVersion,
        string fromVersion,
        CancellationToken cancellationToken);

    Task<FileData?> GetData(
        DatasetVersion datasetVersion,
        FileType type,
        string filePath,
        bool isHeadRequest,
        ByteRange? byteRange,
        bool allowUnpublished,
        CancellationToken cancellationToken);

    IAsyncEnumerable<FileMetadata> List(
        DatasetVersion datasetVersion,
        CancellationToken cancellationToken);

    Task WriteDataAsZip(
        DatasetVersion datasetVersion,
        string[] paths,
        Stream stream,
        bool allowUnpublished,
        CancellationToken cancellationToken);
}
