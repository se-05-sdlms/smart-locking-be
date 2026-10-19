namespace smart_locking_be.Application.Exceptions;

public sealed class ServiceUnavailableException : ApiException
{
    public ServiceUnavailableException(string errorCode, string message, Exception? innerException = null)
        : base(message, errorCode, innerException) { }

}
