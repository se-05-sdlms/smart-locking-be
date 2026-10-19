namespace smart_locking_be.Application.Exceptions;

public sealed class ForbiddenException : ApiException
{
    public ForbiddenException(string errorCode, string message) : base(message, errorCode) { }
}
