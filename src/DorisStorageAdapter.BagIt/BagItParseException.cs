using System;

namespace DorisStorageAdapter.BagIt;

public sealed class BagItParseException : Exception
{
    public BagItParseException() : base()
    {
    }

    public BagItParseException(string message) : base(message)
    {
    }

    public BagItParseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
