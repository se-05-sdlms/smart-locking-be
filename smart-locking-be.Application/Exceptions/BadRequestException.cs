namespace smart_locking_be.Application.Exceptions;

public sealed class BadRequestException : ApiException
{
    public BadRequestException(string message, string? parameterName = null)
        : base(message, "bad_request")
    {
        ParameterName = parameterName;
    }

    public BadRequestException(string message, Exception? innerException)
        : base(message, "bad_request", innerException)
    {
    }

    public BadRequestException(string message, string? parameterName, Exception? innerException)
        : base(message, "bad_request", innerException)
    {
        ParameterName = parameterName;
    }

    public string? ParameterName { get; }
}
