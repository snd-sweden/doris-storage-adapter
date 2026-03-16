using System;
using System.Collections.Generic;

namespace DorisStorageAdapter.Services.Contract.Exceptions;

public sealed class DatasetInconsistentException : ServiceException
{
    private const string TitleValue = "Dataset is inconsistent.";
    private const string MessageValue = "The dataset is in an inconsistent state.";

    public DatasetInconsistentException() : base(TitleValue, MessageValue, [])
    {
    }

    public DatasetInconsistentException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public DatasetInconsistentException(string message) : base(message)
    {
    }

    public DatasetInconsistentException(IEnumerable<ErrorItem> errors) : base(TitleValue, MessageValue, errors)
    {
    }
}
