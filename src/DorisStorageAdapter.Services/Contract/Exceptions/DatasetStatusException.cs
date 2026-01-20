using System;

namespace DorisStorageAdapter.Services.Contract.Exceptions;

public sealed class DatasetStatusException : ServiceException
{
    public DatasetStatusException() : base("Status mismatch.")
    {
    }

    public DatasetStatusException(string message) : base(message)
    {
    }

    public DatasetStatusException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
