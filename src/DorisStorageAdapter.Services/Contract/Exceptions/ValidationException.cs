using System;

namespace DorisStorageAdapter.Services.Contract.Exceptions;

public class ValidationException : ServiceException
{
    public ValidationException() : base()
    {
    }

    public ValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ValidationException(string message) : base(message)
    {
    }
}
