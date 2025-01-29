﻿namespace DorisStorageAdapter.Services.Exceptions;

internal sealed class ConflictException : ApiException
{
    public ConflictException() : base("Write conflict.", 409) 
    {
    }

    public ConflictException(string message) : base(message)
    {
    }

    public ConflictException(string message, System.Exception innerException) : base(message, innerException)
    {
    }
}
