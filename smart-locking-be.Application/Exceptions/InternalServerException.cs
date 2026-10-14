namespace smart_locking_be.Application.Exceptions;

/// <summary>
/// An expected server-side failure whose implementation details must not be exposed.
/// </summary>
public sealed class InternalServerException(
    string message,
    Exception? innerException = null)
    : Exception(message, innerException);
