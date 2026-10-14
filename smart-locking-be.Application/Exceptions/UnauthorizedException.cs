namespace smart_locking_be.Application.Exceptions;

public sealed class UnauthorizedException(string message = "Authentication is required.")
    : ApiException(message, "unauthorized");
