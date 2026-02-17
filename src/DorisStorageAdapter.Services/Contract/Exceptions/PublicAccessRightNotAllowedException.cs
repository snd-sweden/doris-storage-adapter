namespace DorisStorageAdapter.Services.Contract.Exceptions;

public sealed class PublicAccessRightNotAllowedException : ServiceException
{
    public PublicAccessRightNotAllowedException() : base("Public access right not allowed.")
    {
    }

    public PublicAccessRightNotAllowedException(string message, System.Exception innerException) : base(message, innerException)
    {
    }

    public PublicAccessRightNotAllowedException(string message) : base(message)
    {
    }
}
