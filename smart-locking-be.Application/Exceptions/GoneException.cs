namespace smart_locking_be.Application.Exceptions;

public sealed class GoneException : ApiException
{
    public GoneException(string errorCode, string message) : base(message, errorCode) { }
}
