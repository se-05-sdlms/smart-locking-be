namespace smart_locking_be.Application.Exceptions;

public sealed class BadRequestException : ApiException
{
    // ponytail: keep field names in messages until clients need structured field errors.
    public BadRequestException(string message) : base(message, "bad_request") { }

    public BadRequestException(string message, Exception? innerException)
        : base(message, "bad_request", innerException)
    {
    }
}
