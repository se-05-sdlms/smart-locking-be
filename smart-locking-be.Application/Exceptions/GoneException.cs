namespace smart_locking_be.Application.Exceptions;

public sealed class GoneException(string message)
    : ApiException(message, "gone");
