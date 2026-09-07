using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Lettermint.Models;

namespace Lettermint;

public sealed class ClientOptions
{
    public Uri BaseUrl { get; init; } = new("https://api.lettermint.co/v1/");
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public HttpClient? HttpClient { get; init; }
}

public sealed class RequestOptions
{
    public IReadOnlyDictionary<string, string>? Query { get; init; }
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
    public string? IdempotencyKey { get; init; }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ApiOperationAttribute(string surface, string operationId) : Attribute
{
    public string Surface { get; } = surface;
    public string OperationId { get; } = operationId;
}

public class LettermintException(string message) : Exception(message);
public sealed class LettermintApiException(HttpStatusCode statusCode, string responseBody)
    : LettermintException($"Lettermint returned HTTP {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ResponseBody { get; } = responseBody;
}

internal sealed class Transport : IDisposable
{
    private static readonly string SdkVersion = typeof(Transport).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    private readonly string token;
    private readonly bool sending;
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly Uri baseUrl;
    private readonly TimeSpan timeout;
    private bool disposed;

    internal Transport(string token, bool sending, ClientOptions? options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (token.Any(char.IsControl)) throw new ArgumentException("Token contains a control character.", nameof(token));
        options ??= new();
        if (!options.BaseUrl.IsAbsoluteUri || options.BaseUrl.Scheme is not ("https" or "http") ||
            options.BaseUrl.Query.Length != 0 || options.BaseUrl.Fragment.Length != 0 || options.BaseUrl.UserInfo.Length != 0)
            throw new ArgumentException("Base URL must be an HTTP or HTTPS URL without credentials, a query, or a fragment.");
        if (options.Timeout <= TimeSpan.Zero || options.Timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Timeout must be positive and less than 49 days.");
        this.token = token;
        this.sending = sending;
        baseUrl = new Uri(options.BaseUrl.AbsoluteUri.TrimEnd('/') + "/");
        timeout = options.Timeout;
        ownsHttp = options.HttpClient == null;
        http = options.HttpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        CheckDefaultHeaders();
    }

    private void CheckDefaultHeaders()
    {
        if (http.DefaultRequestHeaders.Contains("Authorization") || http.DefaultRequestHeaders.Contains("x-lettermint-token"))
            throw new ArgumentException("The supplied HttpClient must not have default authentication headers.");
    }

    internal static string Segment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value is "." or "..") throw new ArgumentException("A path identifier must not be a dot segment.", nameof(value));
        return Uri.EscapeDataString(value);
    }

    internal async Task<string> RawAsync(HttpMethod method, string path, object? payload, RequestOptions? options, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CheckDefaultHeaders();
        var url = new Uri(baseUrl, path.TrimStart('/')).AbsoluteUri;
        if (options?.Query is { Count: > 0 } query)
            url += "?" + string.Join("&", query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd($"Lettermint/{SdkVersion} (.NET)");
        if (options?.Headers != null)
            foreach (var header in options.Headers)
            {
                if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("x-lettermint-token", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Request headers must not override authentication.");
                request.Headers.Add(header.Key, header.Value);
            }
        if (sending) request.Headers.Add("x-lettermint-token", token);
        else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (options?.IdempotencyKey is { } key)
        {
            request.Headers.Remove("Idempotency-Key");
            request.Headers.Add("Idempotency-Key", key);
        }
        if (payload != null)
            request.Content = new StringContent(JsonSerializer.Serialize(payload, payload.GetType(), LettermintJson.Options), Encoding.UTF8, "application/json");
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new LettermintApiException(response.StatusCode, Redact(body));
            return body;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LettermintException("The request timed out.");
        }
        catch (OperationCanceledException) { throw new OperationCanceledException(cancellationToken); }
        catch (HttpRequestException) { throw new LettermintException("The HTTP request failed."); }
    }

    private string Redact(string value) => value.Replace(token, "[REDACTED]", StringComparison.Ordinal)
        .Replace(JsonSerializer.Serialize(token)[1..^1], "[REDACTED]", StringComparison.Ordinal);

    internal async Task<T> SendAsync<T>(HttpMethod method, string path, object? payload, RequestOptions? options, CancellationToken cancellationToken)
    {
        var body = await RawAsync(method, path, payload, options, cancellationToken).ConfigureAwait(false);
        try { return JsonSerializer.Deserialize<T>(body, LettermintJson.Options) ?? throw new JsonException(); }
        catch (JsonException) { throw new LettermintException("The API returned an invalid JSON response."); }
    }

    internal async Task SendAsync(HttpMethod method, string path, object? payload, RequestOptions? options, CancellationToken cancellationToken)
        => _ = await RawAsync(method, path, payload, options, cancellationToken).ConfigureAwait(false);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ownsHttp) http.Dispose();
    }
}
