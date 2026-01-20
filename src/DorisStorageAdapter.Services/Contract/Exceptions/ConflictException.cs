using System;

namespace DorisStorageAdapter.Services.Contract.Exceptions;

public sealed class ConflictException : ServiceException
{
    public ConflictException() : base("Write conflict.")
    {
    }

    public ConflictException(string message) : base(message)
    {
    }

    public ConflictException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
