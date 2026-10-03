using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lettermint.Models;

namespace Lettermint.Internal;

/// <summary>
/// Sends requests for one client. Holds the tokens in private fields that
/// nothing else in the SDK can read, and that debuggers do not show.
/// </summary>
internal sealed class Transport : IDisposable
{
    public static readonly string UserAgent = "lettermint-dotnet/" + (typeof(Transport).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0");

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly string? _sendingToken;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly string? _teamToken;

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private int _disposed;

    public Transport(string? sendingToken, string? teamToken, string baseUrl, TimeSpan timeout, HttpClient? httpClient)
    {
        _sendingToken = sendingToken;
        _teamToken = teamToken;
        BaseUrl = baseUrl;
        Timeout = timeout;
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
        CheckDefaultHeaders();
    }

    public string BaseUrl { get; }

    public TimeSpan Timeout { get; }

    public bool HasSendingToken => _sendingToken is not null;

    public bool HasTeamToken => _teamToken is not null;

    public bool OwnsHttpClient => _ownsHttp;

    public static string CheckBaseUrl(Uri? value)
    {
        if (value is null || !value.IsAbsoluteUri || (value.Scheme != Uri.UriSchemeHttps && value.Scheme != Uri.UriSchemeHttp))
        {
            throw new LettermintConfigException("BaseUrl must be an absolute http(s) URL.");
        }
        if (value.UserInfo.Length > 0 || value.Query.Length > 0 || value.Fragment.Length > 0)
        {
            throw new LettermintConfigException("BaseUrl must not contain credentials, a query string or a fragment.");
        }
        return value.AbsoluteUri.TrimEnd('/');
    }

    public static TimeSpan CheckTimeout(TimeSpan value, string option = "Timeout")
    {
        if (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue)
        {
            throw new LettermintConfigException($"{option} must be positive and at most {int.MaxValue} milliseconds.");
        }
        return value;
    }

    private void CheckDefaultHeaders()
    {
        if (_http.DefaultRequestHeaders.Contains("Authorization") || _http.DefaultRequestHeaders.Contains("x-lettermint-token"))
        {
            throw new LettermintConfigException("The HttpClient must not have default Authorization or x-lettermint-token headers; pass the tokens in LettermintOptions.");
        }
    }

    /// <summary>Throws if the token for <paramref name="auth"/> is not configured.</summary>
    public void AssertAuth(string label, AuthSurface auth) => AuthHeader(label, auth);

    private (string Name, string Value) AuthHeader(string label, AuthSurface auth)
    {
        var useTeam = auth == AuthSurface.Team || (auth == AuthSurface.Either && _teamToken is not null);
        if (useTeam)
        {
            if (_teamToken is null)
            {
                throw new LettermintConfigException($"{label} needs TeamToken; set LettermintOptions.TeamToken.");
            }
            return ("Authorization", "Bearer " + _teamToken);
        }
        if (_sendingToken is null)
        {
            throw new LettermintConfigException($"{label} needs SendingToken; set LettermintOptions.SendingToken.");
        }
        return ("x-lettermint-token", _sendingToken);
    }

    private static string EncodePathParam(string label, string name, string? value)
    {
        if (string.IsNullOrEmpty(value) || value is "." or "..")
        {
            throw new LettermintConfigException($"{label}: {name} must be a non-empty string other than \".\" and \"..\".");
        }
        return Uri.EscapeDataString(value);
    }

    private static void CheckIdempotencyKey(string key)
    {
        if (key.Length == 0 || key.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
        {
            throw new LettermintValidationException("IdempotencyKey must be a non-empty string without line breaks.", "IdempotencyKey");
        }
    }

    /// <summary>Calls one operation of the table.</summary>
    public Task<TResponse> CallAsync<TQuery, TRequest, TResponse>(
        Operation<TQuery, TRequest, TResponse> operation,
        string label,
        string[] path,
        TQuery? query,
        TRequest? body,
        RequestOptions? options,
        CancellationToken cancellationToken,
        AuthSurface? auth = null)
        => SendAsync<TResponse>(operation.Definition, label, path, QueryString.ToNode(query), body is NoBody ? null : body, options, cancellationToken, auth);

    /// <summary>Follows <c>next_cursor</c> through every page of a cursor-paginated list.</summary>
    public async IAsyncEnumerable<TItem> PaginateAsync<TQuery, TPage, TItem>(
        Operation<TQuery, NoBody, TPage> operation,
        string label,
        string[] path,
        TQuery? query,
        RequestOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TPage : CursorPage<TItem>
    {
        var cursorParam = operation.Definition.Pagination?.CursorParam
            ?? throw new InvalidOperationException($"{operation.Definition.Key} is not cursor-paginated.");
        var baseQuery = QueryString.ToNode(query);
        var current = baseQuery;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var page = await SendAsync<TPage>(operation.Definition, label, path, current, null, options, cancellationToken, null).ConfigureAwait(false);
            if (page.Data is null)
            {
                throw new UnexpectedResponseException($"{label}: the API returned a page without a data array.", HttpStatusCode.OK, string.Empty);
            }
            foreach (var item in page.Data)
            {
                yield return item;
            }
            var next = page.NextCursor;
            if (string.IsNullOrEmpty(next) || !seen.Add(next))
            {
                yield break;
            }
            current = baseQuery is null ? new JsonObject() : (JsonObject)baseQuery.DeepClone();
            current[cursorParam] = next;
        }
    }

    private async Task<TResponse> SendAsync<TResponse>(
        OperationDefinition operation,
        string label,
        string[] pathValues,
        JsonObject? query,
        object? body,
        RequestOptions? options,
        CancellationToken cancellationToken,
        AuthSurface? authOverride)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var (authName, authValue) = AuthHeader(label, authOverride ?? operation.Auth);
        var path = operation.Path;
        for (var index = 0; index < operation.PathParams.Count; index++)
        {
            var name = operation.PathParams[index];
            path = path.Replace("{" + name + "}", EncodePathParam(label, name, index < pathValues.Length ? pathValues[index] : null), StringComparison.Ordinal);
        }
        var timeout = options?.Timeout is { } perCall ? CheckTimeout(perCall) : Timeout;
        string? idempotencyKey = (options as IdempotentRequestOptions)?.IdempotencyKey;
        if (idempotencyKey is not null)
        {
            CheckIdempotencyKey(idempotencyKey);
        }

        var queryString = QueryString.Serialize(query);
        var url = new Uri(BaseUrl + path + (queryString.Length > 0 ? "?" + queryString : string.Empty));
        using var request = new HttpRequestMessage(new HttpMethod(operation.Method), url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }
        if (body is not null)
        {
            var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), LettermintJson.Options));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Content = content;
        }
        request.Headers.TryAddWithoutValidation(authName, authValue);

        cancellationToken.ThrowIfCancellationRequested();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not LettermintException)
        {
            throw Translate(error, timeout, cancellationToken);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (status >= 300 && status < 400)
            {
                throw new RedirectException(response.StatusCode, $"The Lettermint API answered with a redirect (HTTP {status}). Redirects are not followed; check the BaseUrl option.");
            }
            if (response.RequestMessage?.RequestUri is { } finalUri && finalUri != url)
            {
                throw new RedirectException(response.StatusCode, "The HttpClient followed a redirect away from the Lettermint API. Configure its handler with AllowAutoRedirect = false; the SDK never follows redirects.");
            }

            byte[] bytes;
            try
            {
                bytes = await response.Content.ReadAsByteArrayAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not LettermintException)
            {
                throw Translate(error, timeout, cancellationToken);
            }
            return Decode<TResponse>(operation, response, bytes);
        }
    }

    private Exception Translate(Exception error, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new OperationCanceledException("The request was canceled.", error, cancellationToken);
        }
        if (error is OperationCanceledException)
        {
            // Our timeout, or the HttpClient.Timeout of a caller-supplied client.
            var effective = _http.Timeout > TimeSpan.Zero && _http.Timeout < timeout ? _http.Timeout : timeout;
            return new LettermintTimeoutException(effective, error);
        }
        return new ConnectionException(error);
    }

    private static TResponse Decode<TResponse>(OperationDefinition operation, HttpResponseMessage response, byte[] bytes)
    {
        var status = (int)response.StatusCode;
        var text = Encoding.UTF8.GetString(bytes);
        if (status >= 200 && status < 300)
        {
            if (operation.ResponseKind == ResponseKind.Empty || status is 204 or 205)
            {
                return default!;
            }
            if (operation.ResponseKind == ResponseKind.Text)
            {
                return (TResponse)(object)text;
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new UnexpectedResponseException($"The Lettermint API answered with HTTP {status} and an empty body where JSON was expected.", response.StatusCode, text);
            }
            try
            {
                return JsonSerializer.Deserialize<TResponse>(bytes, LettermintJson.Options)
                    ?? throw new UnexpectedResponseException($"The Lettermint API answered with HTTP {status} and a null JSON body.", response.StatusCode, text);
            }
            catch (JsonException error)
            {
                throw new UnexpectedResponseException($"The Lettermint API answered with HTTP {status} and a body that is not valid JSON for {typeof(TResponse).Name}.", response.StatusCode, text, error);
            }
        }
        if (status < 400)
        {
            throw new UnexpectedResponseException($"The Lettermint API answered with an unexpected HTTP status {status}.", response.StatusCode, text);
        }
        JsonElement? body = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                using var document = JsonDocument.Parse(bytes);
                body = document.RootElement.Clone();
            }
            catch (JsonException error)
            {
                var contentType = response.Content.Headers.ContentType?.MediaType;
                throw new UnexpectedResponseException($"The Lettermint API answered with HTTP {status} and a body that is not JSON{(contentType is null ? string.Empty : $" ({contentType})")}.", response.StatusCode, text, error);
            }
        }
        throw CreateApiException(response, body);
    }

    private static LettermintApiException CreateApiException(HttpResponseMessage response, JsonElement? body)
    {
        string? code = null;
        string? message = null;
        JsonElement? details = null;
        IReadOnlyDictionary<string, IReadOnlyList<string>>? errors = null;
        if (body is { ValueKind: JsonValueKind.Object } root)
        {
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object)
                {
                    if (error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String)
                    {
                        code = c.GetString();
                    }
                    if (error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    {
                        message = m.GetString();
                    }
                    if (error.TryGetProperty("details", out var d))
                    {
                        details = d.Clone();
                    }
                }
                else if (error.ValueKind == JsonValueKind.String)
                {
                    code = error.GetString();
                }
            }
            if (string.IsNullOrEmpty(message) && root.TryGetProperty("message", out var top) && top.ValueKind == JsonValueKind.String)
            {
                message = top.GetString();
            }
            if (root.TryGetProperty("errors", out var fieldErrors) && fieldErrors.ValueKind == JsonValueKind.Object)
            {
                var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                foreach (var field in fieldErrors.EnumerateObject())
                {
                    map[field.Name] = field.Value.ValueKind == JsonValueKind.Array
                        ? field.Value.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.GetRawText()).ToList()
                        : [field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString() ?? string.Empty : field.Value.GetRawText()];
                }
                errors = map;
            }
        }
        var status = (int)response.StatusCode;
        if (string.IsNullOrEmpty(message))
        {
            message = string.IsNullOrEmpty(response.ReasonPhrase) ? $"HTTP {status}" : response.ReasonPhrase;
        }
        return status switch
        {
            401 => new AuthenticationException(message, code, details, body),
            403 => new PermissionException(message, code, details, body),
            404 => new NotFoundException(message, code, details, body),
            409 => new ConflictException(message, code, details, body),
            422 => new ValidationException(message, errors, code, details, body),
            429 => new RateLimitException(message, ParseRetryAfter(response), code, details, body),
            >= 500 => new ServerException(response.StatusCode, message, code, details, body),
            _ => new LettermintApiException(response.StatusCode, message, code, details, body),
        };
    }

    internal static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values))
        {
            return null;
        }
        var value = values.FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        if (value.All(char.IsAsciiDigit))
        {
            return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ? TimeSpan.FromSeconds(seconds) : null;
        }
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? TimeSpan.FromSeconds(Math.Ceiling(wait.TotalSeconds)) : TimeSpan.Zero;
        }
        return null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _ownsHttp)
        {
            _http.Dispose();
        }
    }

    public override string ToString() => "Transport";
}
