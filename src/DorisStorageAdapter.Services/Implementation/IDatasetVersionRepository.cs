using DorisStorageAdapter.Services.Contract.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation;

public interface IDatasetVersionRepository
{
    Task<FileMetadata> StoreFile(
        DatasetVersion datasetVersion,
        FileType type,
        string relativePath,
        Stream data,
        long size,
        string? contentType,
        CancellationToken cancellationToken);

    Task DeleteFile(
        DatasetVersion datasetVersion,
        FileType type,
        string relativePath,
        CancellationToken cancellationToken);

    Task ImportFromVersion(
        DatasetVersion datasetVersion,
        string fromVersion,
        CancellationToken cancellationToken);

    Task<FileData?> GetFileData(
        DatasetVersion datasetVersion,
        FileType type,
        string relativePath,
        bool isHeadRequest,
        ByteRange? byteRange,
        bool allowDraft,
        CancellationToken cancellationToken);

    IAsyncEnumerable<FileMetadata> ListFiles(
        DatasetVersion datasetVersion,
        CancellationToken cancellationToken);

    Task WriteFilesAsZip(
        DatasetVersion datasetVersion,
        string[] relativePaths,
        Stream output,
        bool allowDraft,
        CancellationToken cancellationToken);
}