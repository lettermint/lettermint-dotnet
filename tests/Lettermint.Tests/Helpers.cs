using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lettermint.Tests;

internal sealed record RecordedRequest(string Method, Uri Url, IReadOnlyDictionary<string, string> Headers, string? Body)
{
    public JsonNode? Json => Body is null ? null : JsonNode.Parse(Body);

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    /// <summary>The decoded query, in order.</summary>
    public List<KeyValuePair<string, string>> Query =>
        Url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Select(parts => new KeyValuePair<string, string>(Decode(parts[0]), Decode(parts.Length > 1 ? parts[1] : string.Empty)))
            .ToList();

    public Dictionary<string, string> QueryMap => Query.ToDictionary(p => p.Key, p => p.Value);

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
}

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, int, CancellationToken, Task<HttpResponseMessage>> _responder;

    public FakeHandler(Func<RecordedRequest, int, CancellationToken, Task<HttpResponseMessage>> responder) => _responder = responder;

    public List<RecordedRequest> Requests { get; } = [];

    public bool Disposed { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }
        string? body = null;
        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }
            body = await request.Content.ReadAsStringAsync(cancellationToken);
        }
        var recorded = new RecordedRequest(request.Method.Method, request.RequestUri!, headers, body);
        lock (Requests)
        {
            Requests.Add(recorded);
        }
        var response = await _responder(recorded, Requests.Count - 1, cancellationToken);
        response.RequestMessage ??= request;
        return response;
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

internal static class Fake
{
    public const string SendingToken = "lm_sendingTokenForTests0123456789abc";
    public const string TeamToken = "lm_team_teamTokenForTests0123456789xyz";

    public static HttpResponseMessage Json(int status, object? body) => new((HttpStatusCode)status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage Text(int status, string body, string contentType = "text/html") => new((HttpStatusCode)status)
    {
        Content = new StringContent(body, Encoding.UTF8, contentType),
    };

    public static HttpResponseMessage Empty(int status) => new((HttpStatusCode)status) { Content = new ByteArrayContent([]) };

    public static readonly object Sent = new { message_id = "msg_1", status = "pending" };

    public static (LettermintClient Client, FakeHandler Handler) Client(
        Func<RecordedRequest, int, HttpResponseMessage>? respond = null,
        bool sending = true,
        bool team = true,
        TimeSpan? timeout = null)
        => Client((request, index, _) => Task.FromResult((respond ?? DefaultResponse)(request, index)), sending, team, timeout);

    public static (LettermintClient Client, FakeHandler Handler) Client(
        Func<RecordedRequest, int, CancellationToken, Task<HttpResponseMessage>> respond,
        bool sending = true,
        bool team = true,
        TimeSpan? timeout = null)
    {
        var handler = new FakeHandler(respond);
        var client = new LettermintClient(new LettermintOptions
        {
            SendingToken = sending ? SendingToken : null,
            TeamToken = team ? TeamToken : null,
            HttpClient = new HttpClient(handler),
            Timeout = timeout ?? LettermintOptions.DefaultTimeout,
        });
        return (client, handler);
    }

    public static HttpResponseMessage DefaultResponse(RecordedRequest request, int index) =>
        request.Url.AbsolutePath.EndsWith("/send", StringComparison.Ordinal) ? Json(202, Sent)
        : request.Url.AbsolutePath.EndsWith("/send/batch", StringComparison.Ordinal) ? Json(202, new[] { Sent })
        : Json(200, new { data = Array.Empty<object>(), next_cursor = (string?)null });

    public static Models.SendMailRequest Message(string subject = "Hello") => new()
    {
        From = "Acme <hello@acme.test>",
        To = ["jane@example.test"],
        Subject = subject,
        Text = "Hi",
    };
}

/// <summary>A loopback HTTP server for tests that need the real default handler.</summary>
internal sealed class LoopbackServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly Func<HttpListenerContext, Task> _handle;
    private readonly Task _loop;

    public LoopbackServer(Func<HttpListenerContext, Task> handle)
    {
        _handle = handle;
        for (var attempt = 0; ; attempt++)
        {
            var port = Random.Shared.Next(20000, 60000);
            Origin = $"http://127.0.0.1:{port}";
            // On Windows a failed Start() disposes the listener, so every attempt needs a new one.
            var listener = new HttpListener();
            listener.Prefixes.Add(Origin + "/");
            try
            {
                listener.Start();
                _listener = listener;
                break;
            }
            catch (HttpListenerException) when (attempt < 20)
            {
                listener.Close();
            }
        }
        _loop = Task.Run(LoopAsync);
    }

    public string Origin { get; private set; }

    public List<(string Method, string Path, NameValueCollectionView Headers)> Requests { get; } = [];

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                return;
            }
            lock (Requests)
            {
                Requests.Add((context.Request.HttpMethod, context.Request.Url!.AbsolutePath, new NameValueCollectionView(context.Request.Headers)));
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await _handle(context);
                }
                finally
                {
                    try { context.Response.Close(); } catch (Exception) { }
                }
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        _listener.Close();
        try { await _loop; } catch (Exception) { }
    }
}

internal sealed class NameValueCollectionView(System.Collections.Specialized.NameValueCollection headers)
{
    public string? this[string name] => headers[name];

    public IEnumerable<string> Values => headers.AllKeys.Select(key => headers[key] ?? string.Empty);
}
