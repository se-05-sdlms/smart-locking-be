namespace smart_locking_be.Application.Exceptions;

public sealed class ConflictException(string message)
    : ApiException(message, "conflict");
