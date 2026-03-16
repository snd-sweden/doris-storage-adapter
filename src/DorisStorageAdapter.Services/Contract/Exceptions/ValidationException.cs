using System;
using System.Collections.Generic;

namespace DorisStorageAdapter.Services.Contract.Exceptions;

public class ValidationException : ServiceException
{
    private const string TitleValue = "Validation error";
    private const string MessageValue = "One or more validation errors occured.";

    public ValidationException() : base(TitleValue, MessageValue, [])
    {
    }

    public ValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ValidationException(string message) : base(message)
    {
    }

    public ValidationException(IEnumerable<ErrorItem> errors) : base(TitleValue, MessageValue, errors)
    {
    }
}
