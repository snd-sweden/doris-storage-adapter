using DorisStorageAdapter.Services.Exceptions;
using DorisStorageAdapter.Services.Lock;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Storage.FileSystem;

/// <summary>
/// Storage service for storing files on a file system.
/// 
/// This storage service is only fully supported on Linux/Unix.
/// On Windows it will return an error if StoreFile is called
/// for a file that is currently being read.
/// 
/// The file system must be case sensitive, and the file path 
/// for temporary files must be on the same partition as the 
/// base path to ensure atomic file moves.
/// </summary>
/// <param name="configuration">FileSystemStorageService configuration.</param>
/// <param name="lockService">ILockService (used when creating/deleting directories).</param>
internal sealed class FileSystemStorageService(
    IOptions<FileSystemStorageServiceConfiguration> configuration,
    ILockService lockService) : IStorageService
{
    // This is only need for supporting Windows; Linux supports all characters except '/'.
    private static readonly HashSet<char> invalidFileNameChars = [.. Path.GetInvalidFileNameChars()];

    private readonly ILockService lockService = lockService;

    private readonly string basePath = Path.GetFullPath(configuration.Value.BasePath);
    private readonly string tempFilePath = Path.GetFullPath(configuration.Value.TempFilePath);

    public async Task<StorageServiceFileBase> StoreFile(
        string filePath,
        FileData data,
        CancellationToken cancellationToken)
    {
        filePath = GetFullPathOrThrow(filePath);

        string tempFile = Path.Combine(tempFilePath, Guid.NewGuid().ToString());
        string directoryPath = Path.GetDirectoryName(filePath)!;

        try
        {
            await CreateDirectory(directoryPath, cancellationToken);

            using (var stream = new FileStream(tempFile, new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.Create,

                // FileOptions.Asynchronous only has real effect on Windows.
                // It is ignored on Linux where file I/O is always executed
                // synchronously on a background thread (as of 2024-09-11).
                Options = FileOptions.Asynchronous, 

                PreallocationSize = data.Length,

                // The value of Share does not really matter since we are writing to a
                // temporary file that will not be accessed by anyone else.
                Share = FileShare.Read
            }))
            {
                await data.Stream.CopyToAsync(stream, cancellationToken);
            }

            File.Move(tempFile, filePath, true);
        }
        catch
        {
            // Cancelled or failed, try to clean up.

            try
            {
                File.Delete(tempFile);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch { }
#pragma warning restore CA1031

            try
            {
                await DeleteEmptyDirectories(directoryPath, CancellationToken.None);
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
            // Ignore errors here since file has been successfully stored
            // returning the correct modified date is not crucial.
        }
#pragma warning restore CA1031

        return new(
            ContentType: null,
            DateCreated: null,
            DateModified: dateModified ?? DateTime.UtcNow);
    }

    public async Task DeleteFile(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        filePath = GetFullPathOrThrow(filePath);

        var test = new FileInfo(filePath);
        test.Delete();

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
            await DeleteEmptyDirectories(Path.GetDirectoryName(filePath)!, CancellationToken.None);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch
        {
            // Ignore errors here since file has been successfully deleted
            // and deleting empty directories is not crucial.
        }
#pragma warning restore CA1031
    }

    public Task<StorageServiceFile?> GetFileMetadata(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        filePath = GetFullPathOrThrow(filePath);
        var file = new FileInfo(filePath);

        if (file.Exists)
        {
            return Task.FromResult<StorageServiceFile?>(ToStorageServiceFile(file));
        }

        return Task.FromResult<StorageServiceFile?>(null);
    }

    public Task<FileData?> GetFileData(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        filePath = GetFullPathOrThrow(filePath);

        // Explicitly check for existence, since that is much faster
        // than letting the FileStream constructor throw FileNotFoundException.
        if (!File.Exists(filePath))
        {
            return Task.FromResult<FileData?>(null);
        }
        
        try
        {
            var stream = new FileStream(filePath, new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,

                // FileOptions.Asynchronous only has real effect on Windows.
                // It is ignored on Linux where file I/O is always executed
                // synchronously on a background thread (as of 2024-09-11).
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,

                // On Linux we really only have to specify something other than FileShare.None to
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

            return Task.FromResult<FileData?>(new(
                Stream: stream,
                Length: stream.Length,
                ContentType: null));
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<FileData?>(null);
        }
    }

#pragma warning disable CS1998 // This async method lacks 'await'
    public async IAsyncEnumerable<StorageServiceFile> ListFiles(
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
            // Given path is not a directory, try with nearest parent directory.
            directory = new(path[..path.LastIndexOf(Path.DirectorySeparatorChar)]);

            if (!directory.Exists)
            {
                yield break;
            }
        }

        foreach (var file in EnumerateFiles(directory, directory.FullName != path ? path : ""))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return ToStorageServiceFile(file);
        }
    }
#pragma warning restore CS1998

    private string GetFullPathOrThrow(string path)
    {
        static void Throw() => throw new IllegalPathException();

        if (path.Split('/').Any(c => c.Any(invalidFileNameChars.Contains)))
        {
            // This can only happen on Windows; Linux supports all characters except '/'.
            Throw();
        }

        string result = Path.GetFullPath(path, basePath);

        if (!result.StartsWith(basePath, StringComparison.Ordinal))
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

    private StorageServiceFile ToStorageServiceFile(FileInfo file) =>
        new(
            ContentType: null,
            DateCreated: null,
            DateModified: file.LastWriteTimeUtc,
            Path: NormalizePath(Path.GetRelativePath(basePath, file.FullName)),
            Length: file.Length);

    /// <summary>
    /// Returns the root directory of the given directory path
    /// to be used as lock path when creating/deleting directories.
    /// </summary>
    /// <param name="directoryPath">The directory path to get lock path for.</param>
    /// <returns>The lock path (the root directory).</returns>
    private string GetLockPath(string directoryPath)
    {
        string relativePath = NormalizePath(Path.GetRelativePath(basePath, directoryPath));

        int index = relativePath.IndexOf('/', StringComparison.Ordinal) + 1;
        if (index > 0)
        {
            return relativePath[..index];
        }

        return relativePath;
    }

    private Task<IDisposable> LockPath(string directoryPath, CancellationToken cancellationToken) =>
        lockService.LockPath(GetLockPath(directoryPath), cancellationToken);

    private async Task CreateDirectory(string directoryPath, CancellationToken cancellationToken)
    {
        using (await LockPath(directoryPath, cancellationToken))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }

    private async Task DeleteEmptyDirectories(string directoryPath, CancellationToken cancellationToken)
    {
        using (await LockPath(directoryPath, cancellationToken))
        {
            while (directoryPath != basePath)
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
}