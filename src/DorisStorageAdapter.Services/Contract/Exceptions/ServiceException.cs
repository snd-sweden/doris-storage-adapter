using System;
using System.Collections.Generic;

namespace DorisStorageAdapter.Services.Contract.Exceptions;

public abstract class ServiceException: Exception
{
    public IReadOnlyList<ErrorItem> Errors { get; } = [];
    public string? Title { get; }

    protected ServiceException() : base()
    {
    }

    protected ServiceException(string message) : base(message)
    {
        Title = message;
    }

    protected ServiceException(string message, Exception innerException) : base(message, innerException)
    {
        Title = message;
    }

    protected ServiceException(string title, string message, IEnumerable<ErrorItem> errors) : base(message)
    {
        Title = title;
        Errors = [.. errors];
    }
}
