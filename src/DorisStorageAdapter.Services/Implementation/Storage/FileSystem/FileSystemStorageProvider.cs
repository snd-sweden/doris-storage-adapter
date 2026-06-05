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
/// On Windows it can return an error if StoreAsync is called
/// for a file that is currently being read.
/// 
/// The file system must be case sensitive.
/// </summary>
/// <param name="configuration">FileSystemStorageConfiguration configuration.</param>
internal sealed class FileSystemStorageProvider(
    IOptions<FileSystemStorageConfiguration> configuration) : IStorageProvider
{
    // This is only need for supporting Windows; Linux supports all characters except '/'.
    private static readonly HashSet<char> _invalidFileNameChars = [.. Path.GetInvalidFileNameChars()];

    private readonly string _basePath = configuration.Value.BasePath;

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(10),
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
    ];

    public async Task StoreAsync(
        string filePath,
        Stream data,
        long size,
        CancellationToken cancellationToken)
    {
        filePath = GetFullPathOrThrow(filePath);
        string directoryPath = Path.GetDirectoryName(filePath)!;
        string fileName = Path.GetFileName(filePath)!;
        string? tempFilePath = null;

        FileStream OpenTempFileStream()
        {
            const int MaxAttempts = 5;
 
            for (int i = 0; ; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    Directory.CreateDirectory(directoryPath);

                    tempFilePath = Path.Combine(
                        directoryPath,
                        $".{fileName}_{Path.GetRandomFileName()}.tmp");

                    return new FileStream(tempFilePath, new FileStreamOptions
                    {
                        Access = FileAccess.Write,

                        // We must use CreateNew here to ensure that the temp file
                        // does not already exist.
                        Mode = FileMode.CreateNew,

                        // FileOptions.Asynchronous only has real effect on Windows.
                        // It is ignored on Linux where file I/O is always executed
                        // synchronously on a background thread (as of 2024-09-11).
                        Options = FileOptions.Asynchronous,

                        PreallocationSize = size,

                        // The value of Share does not really matter much since writing is done to a
                        // temporary file that will not be accessed by anyone else.
                        Share = FileShare.None
                    });
                }
                catch (Exception e) when (
                    i < MaxAttempts - 1 &&
                    e is IOException or UnauthorizedAccessException)
                {
                    // Probably caused by directory not found or
                    // temp file already exists, retry.

                    // If directory is not found it could be
                    // because another thread removed it in
                    // a call to DeleteEmptyDirectories().
                }
            }
        }

        try
        {
            await using (var stream = OpenTempFileStream())
            {
                await data.CopyToAsync(stream, cancellationToken);

#pragma warning disable CA1849 //  Call async methods when in an async method

                // Flush FileStream buffers and force data to disk (fsync/fdatasync on Linux)
                // so the file contents are durable before move.
                stream.Flush(true);

#pragma warning restore CA1849
            }

            for (int i = 0; ; i++)
            {
                // If file move fails, retry a few times with increasing delay.
                // This is mostly for making move more robust on Windows, where
                // another process reading filePath can prevent the move
                // (e.g. anti virus software).
                try
                {
                    File.Move(tempFilePath!, filePath, true);
                    return;
                }
                catch (Exception e) when (
                    i < RetryDelays.Length &&
                    e is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(RetryDelays[i], cancellationToken);
                }
            }
        }
        catch
        {
            // Cancelled or failed, try to clean up

            if (tempFilePath is not null)
            {
                try
                {
                    File.Delete(tempFilePath);
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch { }
#pragma warning restore CA1031
            }

            try
            {
                DeleteEmptyDirectories(directoryPath);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch { }
#pragma warning restore CA1031

            throw;
        }
    }

    public async Task DeleteAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        filePath = GetFullPathOrThrow(filePath);

        for (int i = 0; ; i++)
        {
            // If file delete fails, retry a few times with increasing delay.
            // This is mostly for making delete more robust on Windows, where
            // another process reading filePath can prevent the delete
            // (e.g. anti virus software).
            try
            {
                File.Delete(filePath);
                break;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception e) when (
                i < RetryDelays.Length &&
                e is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(RetryDelays[i], cancellationToken);
            }
        }

        try
        {
            // Delete any empty subdirectories that result from deleting the file.
            DeleteEmptyDirectories(Path.GetDirectoryName(filePath)!);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch
        {
            // Ignore errors here since file has been successfully deleted
            // and deleting empty directories is not crucial.
        }
#pragma warning restore CA1031
    }

    public Task<StorageFileMetadata?> GetMetadataAsync(
        string filePath,
        CancellationToken cancellationToken)
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

    public Task<StorageFileData?> GetDataAsync(
        string filePath,
        StorageByteRange? byteRange,
        CancellationToken cancellationToken)
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
        bool recursive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        static IEnumerable<FileInfo> EnumerateFiles(
            DirectoryInfo directory, string path, bool recursive)
        {
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (!string.IsNullOrEmpty(path) &&
                    !entry.FullName.StartsWith(path, StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry is DirectoryInfo subDirectory && recursive)
                {
                    foreach (var file in EnumerateFiles(subDirectory, "", recursive))
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

        foreach (var file in EnumerateFiles(
            directory, directory.FullName != path ? path : "", recursive))
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

        if (!IsUnderBasePath(result))
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

    private bool IsUnderBasePath(string path) =>
        path.StartsWith(_basePath, StringComparison.Ordinal);

    private StorageFileMetadata ToStorageFileMetadata(FileInfo file) =>
        new(
            DateCreated: null,
            DateModified: file.LastWriteTimeUtc,
            Path: NormalizePath(Path.GetRelativePath(_basePath, file.FullName)),
            Size: file.Length);

    private void DeleteEmptyDirectories(string directoryPath)
    {
        if (!IsUnderBasePath(directoryPath))
        {
            throw new InvalidOperationException("The directory is outside the storage base path.");
        }

        string rootPath = Path.TrimEndingDirectorySeparator(_basePath);

        while (directoryPath != rootPath)
        {
            try
            {
                // Fast path: avoid exception by checking explicitly if directory is empty.
                using var e = Directory.EnumerateFileSystemEntries(directoryPath).GetEnumerator();
                if (e.MoveNext())
                {
                    break;
                }

                Directory.Delete(directoryPath);
            }
            catch (DirectoryNotFoundException)
            {
                // Already gone → fine, continue upward.
            }
            catch (IOException)
            {
                // Not empty → stop.
                break;
            }
            catch (UnauthorizedAccessException)
            {
                // Not empty / in use → stop.
                break;
            }

            directoryPath = Path.GetDirectoryName(directoryPath)!;
        }
    }
}