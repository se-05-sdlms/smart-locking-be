namespace smart_locking_be.Application.Exceptions;

public sealed class UnauthorizedException : ApiException
{
    public UnauthorizedException(string errorCode, string message) : base(message, errorCode) { }
}
