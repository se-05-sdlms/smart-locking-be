namespace smart_locking_be.Application.Exceptions;

public sealed class TooManyRequestsException : ApiException
{
    public TooManyRequestsException(string errorCode, string message, int? retryAfterSeconds = null) : base(message, errorCode)
        => RetryAfterSeconds = retryAfterSeconds;

    public int? RetryAfterSeconds { get; }
}
