namespace smart_locking_be.Application.Exceptions;

public sealed class NotFoundException : ApiException
{
    public NotFoundException(string errorCode, string message) : base(message, errorCode) { }
}
