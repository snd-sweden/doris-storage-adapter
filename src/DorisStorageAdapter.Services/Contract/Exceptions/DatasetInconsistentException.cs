using System;
using System.Collections.Generic;

namespace DorisStorageAdapter.Services.Contract.Exceptions;

public sealed class DatasetInconsistentException : ServiceException
{
    private const string TitleValue = "Dataset is inconsistent.";

    public DatasetInconsistentException() : base(TitleValue)
    {
    }

    public DatasetInconsistentException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public DatasetInconsistentException(string message) : base(message)
    {
    }

    public DatasetInconsistentException(string message, IEnumerable<ErrorItem> errors) 
        : base(TitleValue, message, errors)
    {
    }
}
