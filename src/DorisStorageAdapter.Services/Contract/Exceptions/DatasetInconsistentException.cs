using System;
using System.Collections.Generic;

namespace DorisStorageAdapter.Services.Contract.Exceptions;

public sealed class DatasetInconsistentException : ServiceException
{
    private const string title = "Dataset is inconsistent.";
    private const string message = "The dataset is in an inconsistent state.";

    public DatasetInconsistentException() : base(title, message, [])
    {
    }

    public DatasetInconsistentException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public DatasetInconsistentException(string message) : base(message)
    {
    }

    public DatasetInconsistentException(IEnumerable<ErrorItem> errors) : base(title, message, errors)
    {
    }
}
