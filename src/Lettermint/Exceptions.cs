using System.Net;
using System.Text.Json;

namespace Lettermint;

/// <summary>
/// Base class of every exception the SDK throws. No exception carries request
/// headers or API tokens.
/// </summary>
public class LettermintException : Exception
{
    /// <summary>Creates an exception with a message.</summary>
    public LettermintException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and the exception that caused it.</summary>
    public LettermintException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The client was configured or called incorrectly: a missing or unrecognised
/// token, a token that the called method cannot use, an invalid option or an
/// invalid path parameter. Thrown before any request is made.
/// </summary>
public sealed class LettermintConfigException : LettermintException
{
    /// <summary>Creates the exception.</summary>
    public LettermintConfigException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The SDK rejected a request before sending it, for example invalid message
/// tags. Unlike <see cref="ValidationException"/>, the API never saw this request.
/// </summary>
public sealed class LettermintValidationException : LettermintException
{
    /// <summary>Creates the exception.</summary>
    public LettermintValidationException(string message, string? field = null)
        : base(message)
    {
        Field = field;
    }

    /// <summary>The offending field, for example <c>tags</c> or <c>messages[2].tags</c>.</summary>
    public string? Field { get; }
}

/// <summary>
/// The API answered with an error status (4xx or 5xx) and a JSON or empty body.
/// Subclasses cover the common statuses.
/// </summary>
public class LettermintApiException : LettermintException
{
    /// <summary>Creates the exception.</summary>
    public LettermintApiException(HttpStatusCode statusCode, string message, string? code = null, JsonElement? details = null, JsonElement? body = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Details = details;
        Body = body;
    }

    /// <summary>The HTTP status code.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Machine-readable error code from <c>{ "error": { "code" } }</c> (or a string <c>error</c>), if the API sent one.</summary>
    public string? Code { get; }

    /// <summary>Additional context from <c>{ "error": { "details" } }</c>, if the API sent any.</summary>
    public JsonElement? Details { get; }

    /// <summary>The decoded JSON error body, or null for an empty body.</summary>
    public JsonElement? Body { get; }
}

/// <summary>HTTP 401: the token is missing, invalid or revoked.</summary>
public sealed class AuthenticationException : LettermintApiException
{
    /// <summary>Creates the exception.</summary>
    public AuthenticationException(string message, string? code = null, JsonElement? details = null, JsonElement? body = null)
        : base(HttpStatusCode.Unauthorized, message, code, details, body)
    {
    }
}

/// <summary>HTTP 403: the token may not perform this action, or the plan lacks the feature.</summary>
public sealed class PermissionException : LettermintApiException
{
    /// <summary>Creates the exception.</summary>
    public PermissionException(string message, string? code = null, JsonElement? details = null, JsonElement? body = null)
        : base(HttpStatusCode.Forbidden, message, code, details, body)
    {
    }
}

/// <summary>HTTP 404: the resource does not exist or is not visible to the token.</summary>
public sealed class NotFoundException : LettermintApiException
{
    /// <summary>Creates the exception.</summary>
    public NotFoundException(string message, string? code = null, JsonElement? details = null, JsonElement? body = null)
        : base(HttpStatusCode.NotFound, message, code, details, body)
    {
    }
}

/// <summary>HTTP 409: the request conflicts with the current state, for example an Idempotency-Key reused with a different body.</summary>
public sealed class ConflictException : LettermintApiException
{
    /// <summary>Creates the exception.</summary>
    public ConflictException(string message, string? code = null, JsonElement? details = null, JsonElement? body = null)
        : base(HttpStatusCode.Conflict, message, code, details, body)
    {
    }
}

/// <summary>HTTP 422: the API rejected the request data.</summary>
public sealed class ValidationException : LettermintApiException
{
    /// <summary>Creates the exception.</summary>
    public ValidationException(string message, IReadOnlyDictionary<string, IReadOnlyList<string>>? errors = null, string? code = null, JsonElement? details = null, JsonElement? body = null)
        : base(HttpStatusCode.UnprocessableEntity, message, code, details, body)
    {
        Errors = errors;
    }

    /// <summary>Field errors from the <c>{ "message", "errors" }</c> body, when the API sent them.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Errors { get; }
}

/// <summary>HTTP 429: too many requests.</summary>
public sealed class RateLimitException : LettermintApiException
{
    /// <summary>Creates the exception.</summary>
    public RateLimitException(string message, TimeSpan? retryAfter = null, string? code = null, JsonElement? details = null, JsonElement? body = null)
        : base(HttpStatusCode.TooManyRequests, message, code, details, body)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>How long to wait, from the <c>Retry-After</c> header (seconds or an HTTP date), when the API sent one.</summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>HTTP 5xx with a JSON or empty body.</summary>
public sealed class ServerException : LettermintApiException
{
    /// <summary>Creates the exception.</summary>
    public ServerException(HttpStatusCode statusCode, string message, string? code = null, JsonElement? details = null, JsonElement? body = null)
        : base(statusCode, message, code, details, body)
    {
    }
}

/// <summary>
/// The request did not complete within the timeout. The timeout covers the
/// response headers and the body. The API may still have processed the request.
/// </summary>
/// <remarks>Named with a prefix so that it does not clash with <see cref="System.TimeoutException"/>.</remarks>
public sealed class LettermintTimeoutException : LettermintException
{
    /// <summary>Creates the exception.</summary>
    public LettermintTimeoutException(TimeSpan timeout, Exception? innerException = null)
        : base($"The request to the Lettermint API timed out after {timeout.TotalMilliseconds:0} ms.", innerException)
    {
        Timeout = timeout;
    }

    /// <summary>The timeout that elapsed.</summary>
    public TimeSpan Timeout { get; }
}

/// <summary>The request could not be sent or the connection failed (DNS, TLS, refused, reset).</summary>
public sealed class ConnectionException : LettermintException
{
    /// <summary>Creates the exception.</summary>
    public ConnectionException(Exception innerException)
        : base("Could not reach the Lettermint API" + (string.IsNullOrEmpty(innerException?.Message) ? "." : $": {innerException.Message}"), innerException)
    {
    }
}

/// <summary>
/// The response could not be decoded: an empty or non-JSON body where JSON was
/// expected, or an error status with a non-JSON body such as a proxy's HTML page.
/// </summary>
public sealed class UnexpectedResponseException : LettermintException
{
    /// <summary>Creates the exception.</summary>
    public UnexpectedResponseException(string message, HttpStatusCode statusCode, string body, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        BodyExcerpt = body.Length > 200 ? body[..200] + "…" : body;
    }

    /// <summary>The HTTP status code.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The first 200 characters of the response body.</summary>
    public string BodyExcerpt { get; }
}

/// <summary>
/// The API answered with a redirect (3xx). The SDK never follows redirects, so
/// that tokens are not sent to another location.
/// </summary>
public sealed class RedirectException : LettermintException
{
    /// <summary>Creates the exception.</summary>
    public RedirectException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// The redirect status, or the final status when a caller-supplied
    /// <see cref="HttpClient"/> followed the redirect itself.
    /// </summary>
    public HttpStatusCode StatusCode { get; }
}
