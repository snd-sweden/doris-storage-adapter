namespace DorisStorageAdapter.Services.Contract.Exceptions;

internal class PublicDataNotAllowedException : ServiceException
{
    public PublicDataNotAllowedException() : base("Public data not allowed.")
    {
    }

    public PublicDataNotAllowedException(string message, System.Exception innerException) : base(message, innerException)
    {
    }

    public PublicDataNotAllowedException(string message) : base(message)
    {
    }
}
