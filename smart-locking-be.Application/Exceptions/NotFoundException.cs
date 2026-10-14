namespace smart_locking_be.Application.Exceptions;

public sealed class NotFoundException(string message)
    : ApiException(message, "not_found");
