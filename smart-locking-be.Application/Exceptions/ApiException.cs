namespace smart_locking_be.Application.Exceptions;

/// <summary>
/// Base type for expected failures that can safely be returned by the HTTP API.
/// </summary>
public abstract class ApiException : Exception
{
    protected ApiException(string message, string errorCode, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
