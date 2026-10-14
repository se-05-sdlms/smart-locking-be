namespace smart_locking_be.Application.Exceptions;

public sealed class ServiceUnavailableException(
    string message,
    Exception? innerException = null)
    : ApiException(message, "service_unavailable", innerException);
