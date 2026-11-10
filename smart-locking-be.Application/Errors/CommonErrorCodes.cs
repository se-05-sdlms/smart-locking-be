namespace smart_locking_be.Application.Errors;

public static class CommonErrorCodes
{
    public const string InvalidInput = "validation/invalid_input";
    public const string AccessDenied = "common/access_denied";
    public const string InvalidRequest = "common/invalid_request";
    public const string InternalError = "common/internal_error";
    public const string RateLimited = "common/rate_limited";
    public const string RequestTimedOut = "common/request_timed_out";
    public const string RouteNotFound = "common/route_not_found";
    public const string MethodNotAllowed = "common/method_not_allowed";
    public const string HttpError = "common/http_error";
}
