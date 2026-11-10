namespace smart_locking_be.Application.Exceptions;

public sealed class TooManyRequestsException : ApiException
{
    public TooManyRequestsException(string errorCode, string message) : base(message, errorCode) { }
}
