using smart_locking_be.Application.Errors;

namespace smart_locking_be.Application.Exceptions;

public sealed class BadRequestException : ApiException
{
    public BadRequestException(string errorCode, string message) : base(message, errorCode) { }

    public BadRequestException(string errorCode, string message, Exception? innerException)
        : base(message, errorCode, innerException) { }

    private BadRequestException(IDictionary<string, string[]> errors, string message)
        : base(message, CommonErrorCodes.InvalidInput) => Errors = errors;

    public IDictionary<string, string[]>? Errors { get; }

    public static BadRequestException For(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] }, message);

    public static BadRequestException For(IDictionary<string, string[]> errors) =>
        new(errors, "One or more validation errors occurred.");
}
