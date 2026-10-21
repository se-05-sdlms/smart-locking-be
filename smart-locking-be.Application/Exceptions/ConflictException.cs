namespace smart_locking_be.Application.Exceptions;

public sealed class ConflictException : ApiException
{
    public ConflictException(string errorCode, string message) : base(message, errorCode) { }
}
