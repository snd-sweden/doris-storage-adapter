using System;
using System.Collections.Generic;

namespace DorisStorageAdapter.Services.Contract.Exceptions;

public sealed class DatasetIntegrityException : ServiceException
{
    private const string TitleValue = "Dataset integrity validation failed.";

    public DatasetIntegrityException() : base(TitleValue)
    {
    }

    public DatasetIntegrityException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public DatasetIntegrityException(string message) : base(message)
    {
    }

    public DatasetIntegrityException(string message, IEnumerable<ErrorItem> errors) 
        : base(TitleValue, message, errors)
    {
    }
}
