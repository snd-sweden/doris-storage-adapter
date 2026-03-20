using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.Storage.FileSystem;

/// <summary>
/// Storage provider for storing files on a file system.
/// 
/// This storage provider is only fully supported on Linux/Unix.
/// On Windows it will return an error if StoreAsync is called
/// for a file that is currently being read.
/// 
/// The file system must be case sensitive, and the file path 
/// for temporary files must be on the same partition as the 
/// base path to ensure atomic file moves.
/// </summary>
/// <param name="configuration">FileSystemStorageConfiguration configuration.</param>
/// <param name="lockProvider">IStorageLockProvider used when creating/deleting directories.</param>
internal sealed class FileSystemStorageProvider(
    IOptions<FileSystemStorageConfiguration> configuration,
    IStorageLockProvider lockProvider) : IStorageProvider
{
    // This is only need for supporting Windows; Linux supports all characters except '/'.
    private static readonly HashSet<char> _invalidFileNameChars = [.. Path.GetInvalidFileNameChars()];

    private readonly IStorageLockProvider _lockProvider = lockProvider;

    private readonly string _basePath = Path.GetFullPath(configuration.Value.BasePath);
    private readonly string _tempFilePath = Path.GetFullPath(configuration.Value.TempFilePath);

    public async Task<StorageFileBaseMetadata> StoreAsync(
        string filePath,
        Stream data,
        long size,
        string? contentType,
        CancellationToken cancellationToken)
    {
        filePath = GetFullPathOrThrow(filePath);

        string tempFile = Path.Combine(_tempFilePath, Guid.NewGuid().ToString());
        string directoryPath = Path.GetDirectoryName(filePath)!;

        try
        {
            await using (var stream = new FileStream(tempFile, new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.Create,

                // FileOptions.Asynchronous only has real effect on Windows.
                // It is ignored on Linux where file I/O is always executed
                // synchronously on a background thread (as of 2024-09-11).
                Options = FileOptions.Asynchronous,

                PreallocationSize = size,

                // The value of Share does not really matter since writing is done to a
                // temporary file that will not be accessed by anyone else.
                Share = FileShare.Read
            }))
            {
                await data.CopyToAsync(stream, cancellationToken);
            }

            await using (await _lockProvider.AcquireAsync(cancellationToken))
            {
                Directory.CreateDirectory(directoryPath);
                File.Move(tempFile, filePath, true);
            }
        }
        catch
        {
            // Cancelled or failed, try to clean up

            try
            {
                File.Delete(tempFile);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch { }
#pragma warning restore CA1031

            try
            {
                await DeleteEmptyDirectoriesAsync(directoryPath, CancellationToken.None);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch { }
#pragma warning restore CA1031

            throw;
        }

        DateTime? dateModified = null;
        try
        {
            dateModified = File.GetLastWriteTimeUtc(filePath);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch
        {
            // Ignore errors here since file has been successfully stored and
            // returning the correct modified date is not crucial.
        }
#pragma warning restore CA1031

        return new(
            ContentType: null,
            DateCreated: null,
            DateModified: dateModified ?? DateTime.UtcNow);
    }

    public async Task DeleteAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        filePath = GetFullPathOrThrow(filePath);

        try
        {
            File.Delete(filePath);
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        try
        {
            // Delete any empty subdirectories that result from deleting the file.
            await DeleteEmptyDirectoriesAsync(Path.GetDirectoryName(filePath)!, CancellationToken.None);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch
        {
            // Ignore errors here since file has been successfully deleted
            // and deleting empty directories is not crucial.
        }
#pragma warning restore CA1031
    }

    public Task<StorageFileMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        filePath = GetFullPathOrThrow(filePath);
        var file = new FileInfo(filePath);

        if (file.Exists)
        {
            return Task.FromResult<StorageFileMetadata?>(ToStorageFileMetadata(file));
        }

        return Task.FromResult<StorageFileMetadata?>(null);
    }

    public Task<StorageFileData?> GetDataAsync(string filePath, StorageByteRange? byteRange, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        filePath = GetFullPathOrThrow(filePath);

        // Explicitly check for existence, since that is much faster
        // than letting the FileStream constructor throw FileNotFoundException.
        if (!File.Exists(filePath))
        {
            return Task.FromResult<StorageFileData?>(null);
        }

        try
        {
            Stream stream = new FileStream(filePath, new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,

                // FileOptions.Asynchronous only has real effect on Windows.
                // It is ignored on Linux where file I/O is always executed
                // synchronously on a background thread (as of 2024-09-11).
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,

                // On Linux it is only really necessary to specify something other than FileShare.None to
                // ensure that simultaneous calls to GetFileData for the same file succeeds.
                // FileShare.None would result in an (advisory) exclusive file lock which would prevent
                // multiple readers (unless DOTNET_SYSTEM_IO_DISABLEFILELOCKING is true).
                // Simultaneous calls to StoreFile or DeleteFile will succeed regardless of the value of
                // Share here, since File.Move() and File.Delete() does not check for file locks under Linux.

                // On Windows the specified FileShare.Delete ensures that a simultaneous call to DeleteFile
                // will succeed. It is not possible on Windows to allow overwriting the file
                // with File.Move() when it is open for reading here, which means that StoreFile will fail
                // if the file is being read simultaneously.
                Share = FileShare.Read | FileShare.Write | FileShare.Delete
            });

            long length = stream.Length;

            if (byteRange != null)
            {
                stream = StreamHelpers.CreateByteRangeStream(stream, byteRange);
            }

            return Task.FromResult<StorageFileData?>(new(
                ContentType: null,
                Size: length,
                Stream: stream,
                StreamLength: stream.Length));
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<StorageFileData?>(null);
        }
    }

#pragma warning disable CS1998 // This async method lacks 'await'
    public async IAsyncEnumerable<StorageFileMetadata> ListAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        static IEnumerable<FileInfo> EnumerateFiles(DirectoryInfo directory, string path)
        {
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (!string.IsNullOrEmpty(path) && !entry.FullName.StartsWith(path, StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry is DirectoryInfo subDirectory)
                {
                    foreach (var file in EnumerateFiles(subDirectory, ""))
                    {
                        yield return file;
                    }
                }
                else if (entry is FileInfo file)
                {
                    yield return file;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        path = GetFullPathOrThrow(path);
        var directory = new DirectoryInfo(path);

        if (!directory.Exists)
        {
            // Given path is not a directory, try with nearest parent directory
            directory = new(path[..path.LastIndexOf(Path.DirectorySeparatorChar)]);

            if (!directory.Exists)
            {
                yield break;
            }
        }

        foreach (var file in EnumerateFiles(directory, directory.FullName != path ? path : ""))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return ToStorageFileMetadata(file);
        }
    }
#pragma warning restore CS1998

    private string GetFullPathOrThrow(string path)
    {
        static void Throw() => throw new InvalidFileSystemPathException();

        if (path.Split('/').Any(c => c.Any(_invalidFileNameChars.Contains)))
        {
            // This can only happen on Windows; Linux supports all characters except '/'
            Throw();
        }

        string result = Path.GetFullPath(path, _basePath);

        if (!result.StartsWith(_basePath, StringComparison.Ordinal))
        {
            Throw();
        }

        return result;
    }

    private static string NormalizePath(string path)
    {
        if (Path.DirectorySeparatorChar != '/')
        {
            return path.Replace(Path.DirectorySeparatorChar, '/');
        }

        return path;
    }

    private StorageFileMetadata ToStorageFileMetadata(FileInfo file) =>
        new(
            ContentType: null,
            DateCreated: null,
            DateModified: file.LastWriteTimeUtc,
            Path: NormalizePath(Path.GetRelativePath(_basePath, file.FullName)),
            Size: file.Length);

    private async Task DeleteEmptyDirectoriesAsync(string directoryPath, CancellationToken cancellationToken)
    {
        await using var _ = await _lockProvider.AcquireAsync(cancellationToken);

        while (directoryPath != _basePath)
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
                {
                    return;
                }

                Directory.Delete(directoryPath);
            }
            catch (DirectoryNotFoundException) { }

            directoryPath = Path.GetDirectoryName(directoryPath)!;
        }
    }
}