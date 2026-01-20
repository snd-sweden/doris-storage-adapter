using System;

namespace DorisStorageAdapter.Services.Implementation.Storage.FileSystem;

public class InvalidFileSystemPathException : Exception
{
    public InvalidFileSystemPathException()
    {
    }

    public InvalidFileSystemPathException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public InvalidFileSystemPathException(string message) : base(message)
    {
    }
}
