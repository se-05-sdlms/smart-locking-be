namespace smart_locking_be.Application.Exceptions;

public sealed class ForbiddenException(string message = "You do not have permission to perform this action.")
    : ApiException(message, "forbidden");
