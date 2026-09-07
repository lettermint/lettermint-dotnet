using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Lettermint.Models;
using Xunit;

namespace Lettermint.Tests;

public class ClientTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; }
            = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        public bool Disposed { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Respond(request, cancellationToken);
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body) };
    private static ClientOptions Options(HttpClient http) => new() { HttpClient = http, BaseUrl = new("https://example.test/v1") };

    [Fact]
    public async Task UserAgentContainsTheBuildVersion()
    {
        var expectedVersion = typeof(LettermintClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        var handler = new Handler
        {
            Respond = (request, _) =>
            {
                Assert.Equal($"Lettermint/{expectedVersion} (.NET)", request.Headers.UserAgent.ToString());
                return Task.FromResult(Response("pong"));
            }
        };
        using var http = new HttpClient(handler);
        using var email = LettermintClient.Email("secret", Options(http));
        Assert.Equal("pong", await email.PingAsync());
    }

    [Fact]
    public async Task EverySpecOperationHasATypedMethodAndCorrectWireMapping()
    {
        var methods = typeof(ApiClient).Assembly.GetTypes().SelectMany(t => t.GetMethods())
            .Where(m => m.GetCustomAttribute<ApiOperationAttribute>() != null)
            .ToDictionary(m => (m.GetCustomAttribute<ApiOperationAttribute>()!.Surface, m.GetCustomAttribute<ApiOperationAttribute>()!.OperationId));
        int expectedCount = 0;
        foreach (var surface in new[] { "sending", "team" })
        {
            using var spec = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "specs", surface + "-openapi.json")));
            foreach (var path in spec.RootElement.GetProperty("paths").EnumerateObject())
                foreach (var operation in path.Value.EnumerateObject())
                {
                    if (!new[] { "get", "post", "put", "patch", "delete" }.Contains(operation.Name)) continue;
                    expectedCount++;
                    string id = operation.Value.GetProperty("operationId").GetString()!;
                    Assert.True(methods.TryGetValue((surface, id), out var method), id);
                    var parameters = method!.GetParameters();
                    var args = new object?[parameters.Length];
                    string expectedPath = path.Name;
                    object? payload = null;
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var p = parameters[i];
                        if (p.ParameterType == typeof(string))
                        {
                            args[i] = "id /?#é";
                            expectedPath = expectedPath.Replace("{" + p.Name + "}", Uri.EscapeDataString((string)args[i]!));
                        }
                        else if (p.Name == "payload")
                        {
                            payload = Activator.CreateInstance(p.ParameterType);
                            if (payload is ApiModel model)
                                model.AdditionalProperties = new() { ["test_marker"] = JsonSerializer.SerializeToElement("payload") };
                            if (payload is System.Collections.IList list)
                                list.Add(Activator.CreateInstance(p.ParameterType.GetGenericArguments()[0]));
                            args[i] = payload;
                        }
                        else if (p.ParameterType == typeof(RequestOptions))
                            args[i] = new RequestOptions { Query = new Dictionary<string, string> { ["filter[search]"] = "a+b /" }, IdempotencyKey = "test-key" };
                        else args[i] = CancellationToken.None;
                    }
                    var handler = new Handler();
                    using var http = new HttpClient(handler);
                    using var email = LettermintClient.Email("sending-secret", Options(http));
                    using var api = LettermintClient.Api("api-secret", Options(http));
                    var returnType = method.ReturnType.IsGenericType ? method.ReturnType.GetGenericArguments()[0] : null;
                    Assert.NotEqual(typeof(object), returnType);
                    Assert.NotEqual(typeof(JsonElement), returnType);
                    string body = returnType == typeof(string) ? (id == "v1.ping" ? "  pong\n" : "  raw\r\nsource  ")
                        : returnType == null ? "" : JsonSerializer.Serialize(Activator.CreateInstance(returnType), LettermintJson.Options);
                    bool called = false;
                    handler.Respond = async (request, _) =>
                    {
                        called = true;
                        Assert.Equal(operation.Name.ToUpperInvariant(), request.Method.Method);
                        Assert.Equal("/v1" + expectedPath, request.RequestUri!.AbsolutePath);
                        Assert.Equal("?filter%5Bsearch%5D=a%2Bb%20%2F", request.RequestUri.Query);
                        Assert.Equal("test-key", request.Headers.GetValues("Idempotency-Key").Single());
                        Assert.Equal(surface == "sending", request.Headers.Contains("x-lettermint-token"));
                        Assert.Equal(surface == "team", request.Headers.Contains("Authorization"));
                        Assert.Equal(surface == "sending" ? "sending-secret" : "Bearer api-secret",
                            request.Headers.GetValues(surface == "sending" ? "x-lettermint-token" : "Authorization").Single());
                        if (payload != null)
                            Assert.Equal(JsonSerializer.Serialize(payload, payload.GetType(), LettermintJson.Options), await request.Content!.ReadAsStringAsync());
                        else Assert.Null(request.Content);
                        return Response(body);
                    };
                    object target = method.DeclaringType == typeof(EmailClient) ? email : method.DeclaringType == typeof(ApiClient) ? api
                        : typeof(ApiClient).GetProperties().Single(p => p.PropertyType == method.DeclaringType).GetValue(api)!;
                    var task = (Task)method.Invoke(target, args)!;
                    await task;
                    Assert.True(called);
                    if (returnType == typeof(string))
                        Assert.Equal(id == "v1.ping" ? "pong" : body, task.GetType().GetProperty("Result")!.GetValue(task));
                }
        }
        Assert.Equal(expectedCount, methods.Count);
    }

    [Fact]
    public async Task FluentBuilderKeepsMessagesSeparateAndSendsAllFields()
    {
        var bodies = new List<JsonElement>();
        var handler = new Handler
        {
            Respond = async (r, _) =>
        {
            bodies.Add(JsonDocument.Parse(await r.Content!.ReadAsStringAsync()).RootElement.Clone());
            if (bodies.Count == 1) Assert.Equal("key", r.Headers.GetValues("Idempotency-Key").Single());
            else Assert.False(r.Headers.Contains("Idempotency-Key"));
            return Response("{\"message_id\":\"m1\",\"status\":\"queued\"}", HttpStatusCode.Accepted);
        }
        };
        using var http = new HttpClient(handler);
        using var email = LettermintClient.Email("secret", Options(http));
        var first = email.From("a@example.com").To("b@example.com").Subject("One").Html("<p>Hi</p>").Text("Hi there")
            .Cc("c@example.com").Bcc("d@example.com").ReplyTo("reply@example.com").Route("outbound").Tag("welcome")
            .Attach("file.txt", "aGk=", "inline", "text/plain")
            .Headers(new Dictionary<string, string> { ["X-Test"] = "1" })
            .Metadata(new Dictionary<string, string> { ["customer"] = "123" })
            .Settings(new() { TrackOpens = false }).IdempotencyKey("key");
        var second = email.From("a@example.com").To("e@example.com").Subject("Two");
        Assert.Equal(MessageStatus.Queued, (await first.SendAsync()).Status);
        await second.SendAsync();
        Assert.Equal("text/plain", bodies[0].GetProperty("attachments")[0].GetProperty("content_type").GetString());
        Assert.False(bodies[0].GetProperty("settings").GetProperty("track_opens").GetBoolean());
        Assert.Equal("123", bodies[0].GetProperty("metadata").GetProperty("customer").GetString());
        Assert.False(bodies[1].TryGetProperty("attachments", out _));
        Assert.False(bodies[1].TryGetProperty("metadata", out _));
    }

    [Fact]
    public async Task RawPayloadPreservesExplicitNullAndBatchPreservesMaps()
    {
        var handler = new Handler();
        using var http = new HttpClient(handler);
        using var api = LettermintClient.Api("secret", Options(http));
        handler.Respond = async (r, _) =>
        {
            Assert.Equal("{\"name\":null}", await r.Content!.ReadAsStringAsync());
            return Response("{}");
        };
        await api.Projects.UpdateAsync("p", JsonSerializer.SerializeToElement(new { name = (string?)null }));
        using var email = LettermintClient.Email("secret", Options(http));
        handler.Respond = async (r, _) =>
        {
            using var json = JsonDocument.Parse(await r.Content!.ReadAsStringAsync());
            Assert.Equal("1", json.RootElement[0].GetProperty("headers").GetProperty("X-Test").GetString());
            return Response("[]");
        };
        await email.SendBatchAsync(new SendMailRequest[] { new() { Headers = new() { ["X-Test"] = "1" } } });
    }

    [Fact]
    public void HydratesPagesArraysNestedModelsEnumsAndUnknownFields()
    {
        var page = JsonSerializer.Deserialize<MessageIndexResponse>("{\"data\":[{\"id\":\"m1\",\"status\":\"hard_bounced\"}],\"next_cursor\":\"abc\",\"future\":42}", LettermintJson.Options)!;
        Assert.Equal(MessageStatus.HardBounced, page.Data![0].Status);
        Assert.Equal("abc", page.NextCursor);
        Assert.Equal(42, page.ToJson().GetProperty("future").GetInt32());
        Assert.Equal("hard_bounced", page.ToJson().GetProperty("data")[0].GetProperty("status").GetString());
        var route = JsonSerializer.Deserialize<RouteData>("{\"statistics\":[],\"settings\":{\"track_opens\":false,\"attachment_delivery\":\"url\",\"future\":true}}", LettermintJson.Options)!;
        Assert.Empty(route.Statistics!);
        Assert.False(route.Settings!.TrackOpens);
        Assert.Equal(AttachmentDelivery.Url, route.Settings.AttachmentDelivery);
        Assert.True(route.Settings.AdditionalProperties!["future"].GetBoolean());
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("X-LETTERMint-TOKEN")]
    public async Task RejectsAuthenticationOverrides(string header)
    {
        using var http = new HttpClient(new Handler());
        using var email = LettermintClient.Email("secret", Options(http));
        await Assert.ThrowsAsync<ArgumentException>(() => email.PingAsync(new RequestOptions { Headers = new Dictionary<string, string> { [header] = "override" } }));
        http.DefaultRequestHeaders.Add(header, "other-secret");
        await Assert.ThrowsAsync<ArgumentException>(() => email.PingAsync());
        Assert.Throws<ArgumentException>(() => LettermintClient.Api("secret", Options(http)));
    }

    [Fact]
    public async Task ErrorsDoNotExposeTokensAndKeepStatus()
    {
        var handler = new Handler { Respond = (_, _) => Task.FromResult(Response("{\"message\":\"secret-token\"}", HttpStatusCode.UnprocessableEntity)) };
        using var http = new HttpClient(handler);
        using var api = LettermintClient.Api("secret-token", Options(http));
        var error = await Assert.ThrowsAsync<LettermintApiException>(() => api.PingAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, error.StatusCode);
        Assert.DoesNotContain("secret-token", error.ResponseBody);
        Assert.DoesNotContain("secret-token", error.ToString());
        handler.Respond = (_, _) => throw new HttpRequestException("secret-token");
        var network = await Assert.ThrowsAsync<LettermintException>(() => api.PingAsync());
        Assert.DoesNotContain("secret-token", network.ToString());
        handler.Respond = (_, _) => Task.FromResult(Response("secret-token"));
        var invalid = await Assert.ThrowsAsync<LettermintException>(() => api.Domains.ListAsync());
        Assert.DoesNotContain("secret-token", invalid.ToString());
    }

    [Fact]
    public async Task SupportsCancellationTimeoutAndCallerOwnedHttpClient()
    {
        var handler = new Handler { Respond = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response(""); } };
        using var http = new HttpClient(handler);
        using var api = LettermintClient.Api("secret", new ClientOptions { HttpClient = http, Timeout = TimeSpan.FromMilliseconds(20) });
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.PingAsync(cancellationToken: canceled.Token));
        var timeout = await Assert.ThrowsAsync<LettermintException>(() => api.PingAsync());
        Assert.Contains("timed out", timeout.Message);
        api.Dispose();
        Assert.False(handler.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => api.PingAsync());
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("")]
    public async Task RejectsInvalidPathIdentifiers(string id)
    {
        using var http = new HttpClient(new Handler());
        using var api = LettermintClient.Api("secret", Options(http));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => api.Domains.RetrieveAsync(id));
    }
}
