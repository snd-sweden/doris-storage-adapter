using System;
using System.Collections.Generic;

namespace DorisStorageAdapter.Services.Contract.Exceptions;

public class ValidationException : ServiceException
{
    private const string _title = "Validation error";
    private const string _message = "One or more validation errors occured.";

    public ValidationException() : base(_title, _message, [])
    {
    }

    public ValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ValidationException(string message) : base(message)
    {
    }

    public ValidationException(IEnumerable<ErrorItem> errors) : base(_title, _message, errors)
    {
    }
}
