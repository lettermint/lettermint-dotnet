using System.Net;
using System.Text;
using Lettermint.Models;

namespace Lettermint.Tests;

public class TransportTests
{
    [Fact]
    public async Task EmptyAndInvalid2xxBodiesAreUnexpectedResponses()
    {
        var (client, _) = Fake.Client((_, index) => index == 0 ? Fake.Empty(202) : Fake.Text(202, "not json", "application/json"));
        var empty = await Assert.ThrowsAsync<UnexpectedResponseException>(() => client.Emails.SendAsync(Fake.Message()));
        Assert.Equal(HttpStatusCode.Accepted, empty.StatusCode);
        Assert.Contains("empty body", empty.Message);
        var invalid = await Assert.ThrowsAsync<UnexpectedResponseException>(() => client.Emails.SendAsync(Fake.Message()));
        Assert.Equal(HttpStatusCode.Accepted, invalid.StatusCode);
        Assert.Equal("not json", invalid.BodyExcerpt);
    }

    [Fact]
    public async Task AnHtmlErrorPageIsAnUnexpectedResponse()
    {
        var html = "<html><head><title>502 Bad Gateway</title></head><body>" + new string('x', 300) + "</body></html>";
        var (client, _) = Fake.Client((_, _) => Fake.Text(502, html, "text/html"));
        var error = await Assert.ThrowsAsync<UnexpectedResponseException>(() => client.Emails.SendAsync(Fake.Message()));
        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.Equal("The Lettermint API answered with HTTP 502 and a body that is not JSON (text/html).", error.Message);
        Assert.Equal(201, error.BodyExcerpt.Length);
    }

    [Fact]
    public async Task MapsErrorStatusesToTypedExceptions()
    {
        var bodies = new Dictionary<int, object?>
        {
            [400] = new { error = "BAD_REQUEST" },
            [401] = new { message = "Unauthenticated." },
            [403] = new { error = new { code = "FEATURE_NOT_AVAILABLE", message = "Upgrade your plan.", details = new { feature = "x" } } },
            [404] = new { message = "Not found" },
            [409] = new { message = "Idempotency key reused." },
            [418] = null,
            [422] = new { message = "The to field is required.", errors = new { to = new[] { "The to field is required." } } },
            [429] = new { message = "Too many requests." },
            [503] = new { message = "Down" },
        };
        foreach (var (status, body) in bodies)
        {
            var (client, handler) = Fake.Client((_, _) =>
            {
                var response = body is null ? Fake.Empty(status) : Fake.Json(status, body);
                if (status == 429)
                {
                    response.Headers.Add("Retry-After", "7");
                }
                return response;
            });
            var error = await Assert.ThrowsAnyAsync<LettermintApiException>(() => client.Emails.SendAsync(Fake.Message()));
            Assert.Equal(status, (int)error.StatusCode);
            Assert.Single(handler.Requests);
            switch (status)
            {
                case 400:
                    Assert.Equal(typeof(LettermintApiException), error.GetType());
                    Assert.Equal("BAD_REQUEST", error.Code);
                    Assert.Equal("Bad Request", error.Message);
                    break;
                case 401:
                    Assert.IsType<AuthenticationException>(error);
                    Assert.Equal("Unauthenticated.", error.Message);
                    break;
                case 403:
                    Assert.IsType<PermissionException>(error);
                    Assert.Equal("FEATURE_NOT_AVAILABLE", error.Code);
                    Assert.Equal("Upgrade your plan.", error.Message);
                    Assert.Equal("x", error.Details!.Value.GetProperty("feature").GetString());
                    Assert.Equal("Upgrade your plan.", error.Body!.Value.GetProperty("error").GetProperty("message").GetString());
                    break;
                case 404:
                    Assert.IsType<NotFoundException>(error);
                    break;
                case 409:
                    Assert.IsType<ConflictException>(error);
                    break;
                case 418:
                    Assert.Equal(typeof(LettermintApiException), error.GetType());
                    Assert.Null(error.Body);
                    break;
                case 422:
                    var validation = Assert.IsType<ValidationException>(error);
                    Assert.Equal(["The to field is required."], validation.Errors!["to"]);
                    break;
                case 429:
                    Assert.Equal(TimeSpan.FromSeconds(7), Assert.IsType<RateLimitException>(error).RetryAfter);
                    break;
                case 503:
                    Assert.IsType<ServerException>(error);
                    break;
            }
        }
    }

    [Fact]
    public async Task ReadsRetryAfterAsAnHttpDate()
    {
        var (client, _) = Fake.Client((_, _) =>
        {
            var response = Fake.Json(429, new { message = "Slow down" });
            response.Headers.TryAddWithoutValidation("Retry-After", DateTimeOffset.UtcNow.AddSeconds(30).ToString("R"));
            return response;
        });
        var error = await Assert.ThrowsAsync<RateLimitException>(() => client.PingAsync());
        Assert.InRange(error.RetryAfter!.Value.TotalSeconds, 25, 31);
    }

    [Fact]
    public async Task ReturnsNothingFor204AndRawStringsForTextEndpoints()
    {
        var source = "Received: from x\r\nSubject: Hi\r\n\r\nBody  \n";
        var (client, _) = Fake.Client((request, _) => request.Url.AbsolutePath.EndsWith("/report-forwarding") ? new HttpResponseMessage(HttpStatusCode.NoContent) : Fake.Text(200, source, "message/rfc822"));
        await client.Projects.ReportForwarding.DeleteAsync("p");
        Assert.Equal(source, await client.Messages.SourceAsync("m"));
        Assert.Equal(source, await client.Messages.HtmlAsync("m"));
        Assert.Equal(source, await client.Messages.TextAsync("m"));
    }

    [Fact]
    public async Task ARedirectIsAnErrorAndIsNotFollowed()
    {
        var (client, handler) = Fake.Client((_, _) =>
        {
            var response = Fake.Json(307, new { message = "Moved" });
            response.Headers.Location = new Uri("https://elsewhere.test/v1/send");
            return response;
        });
        var error = await Assert.ThrowsAsync<RedirectException>(() => client.Emails.SendAsync(Fake.Message()));
        Assert.Equal(HttpStatusCode.TemporaryRedirect, error.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TheDefaultHandlerNeverFollowsRedirects()
    {
        await using var foreign = new LoopbackServer(context =>
        {
            context.Response.StatusCode = 200;
            return Task.CompletedTask;
        });
        await using var api = new LoopbackServer(context =>
        {
            context.Response.StatusCode = 307;
            context.Response.RedirectLocation = foreign.Origin + "/v1/ping";
            return Task.CompletedTask;
        });
        using var client = new LettermintClient(new LettermintOptions { SendingToken = Fake.SendingToken, TeamToken = Fake.TeamToken, BaseUrl = new Uri(api.Origin + "/v1") });
        var error = await Assert.ThrowsAsync<RedirectException>(() => client.PingAsync());
        Assert.Equal(HttpStatusCode.TemporaryRedirect, error.StatusCode);
        await Assert.ThrowsAsync<RedirectException>(() => client.Emails.SendAsync(Fake.Message()));
        Assert.Empty(foreign.Requests);
        Assert.Equal(2, api.Requests.Count);
    }

    [Fact]
    public async Task DetectsARedirectFollowedByACallerSuppliedHttpClient()
    {
        await using var foreign = new LoopbackServer(async context =>
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/plain";
            await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("pong"));
        });
        await using var api = new LoopbackServer(context =>
        {
            context.Response.StatusCode = 307;
            context.Response.RedirectLocation = foreign.Origin + "/v1/ping";
            return Task.CompletedTask;
        });
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        using var client = new LettermintClient(new LettermintOptions { TeamToken = Fake.TeamToken, BaseUrl = new Uri(api.Origin + "/v1"), HttpClient = http });
        var error = await Assert.ThrowsAsync<RedirectException>(() => client.PingAsync());
        Assert.Contains("AllowAutoRedirect = false", error.Message);
    }

    [Fact]
    public async Task TimesOutWaitingForHeaders()
    {
        var (client, _) = Fake.Client(async (_, _, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return Fake.Json(202, Fake.Sent);
        }, timeout: TimeSpan.FromMilliseconds(100));
        var error = await Assert.ThrowsAsync<LettermintTimeoutException>(() => client.Emails.SendAsync(Fake.Message()));
        Assert.Equal(TimeSpan.FromMilliseconds(100), error.Timeout);
        Assert.Equal("The request to the Lettermint API timed out after 100 ms.", error.Message);
    }

    [Fact]
    public async Task TheTimeoutCoversTheBody()
    {
        var (client, _) = Fake.Client((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StreamContent(new SlowStream()) }), timeout: TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAsync<LettermintTimeoutException>(() => client.Emails.SendAsync(Fake.Message()));
    }

    [Fact]
    public async Task APerCallTimeoutOverridesTheClientTimeout()
    {
        var (client, _) = Fake.Client(async (_, _, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return Fake.Text(200, "pong");
        });
        var error = await Assert.ThrowsAsync<LettermintTimeoutException>(() => client.PingAsync(new RequestOptions { Timeout = TimeSpan.FromMilliseconds(50) }));
        Assert.Equal(TimeSpan.FromMilliseconds(50), error.Timeout);
        await Assert.ThrowsAsync<LettermintConfigException>(() => client.PingAsync(new RequestOptions { Timeout = TimeSpan.Zero }));
    }

    [Fact]
    public async Task CancellationThrowsOperationCanceledException()
    {
        var (client, handler) = Fake.Client(async (_, _, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return Fake.Text(200, "pong");
        });
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PingAsync(cancellationToken: source.Token));
        Assert.IsNotAssignableFrom<LettermintException>(error);
        Assert.Equal(source.Token, error.CancellationToken);

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PingAsync(cancellationToken: canceled.Token));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ConnectionFailuresAreConnectionExceptions()
    {
        var (client, _) = Fake.Client((_, _, _) => throw new HttpRequestException("Connection refused (127.0.0.1:9)"));
        var error = await Assert.ThrowsAsync<ConnectionException>(() => client.PingAsync());
        Assert.Equal("Could not reach the Lettermint API: Connection refused (127.0.0.1:9)", error.Message);
        Assert.IsType<HttpRequestException>(error.InnerException);
    }

    [Fact]
    public async Task UnknownEnumValuesAndFieldsDecode()
    {
        var (client, _) = Fake.Client((_, _) => Fake.Json(202, new { message_id = "m", status = "some_future_status", some_future_field = new { nested = new[] { 1, 2 } } }));
        var result = await client.Emails.SendAsync(Fake.Message());
        Assert.Equal("some_future_status", result.Status.ToString());
        Assert.False(result.Status.IsKnown);
        Assert.Equal("[1,2]", result.AdditionalProperties!["some_future_field"].GetProperty("nested").GetRawText());
    }

    [Fact]
    public async Task NeverRetries()
    {
        var (client, handler) = Fake.Client((_, _) => Fake.Json(503, new { message = "Down" }));
        await Assert.ThrowsAsync<ServerException>(() => client.Domains.ListAsync());
        await Assert.ThrowsAsync<ServerException>(() => client.Emails.SendAsync(Fake.Message()));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task DecodesAMissingDocumentedFieldInsteadOfFailing()
    {
        var (client, _) = Fake.Client((_, _) => Fake.Json(200, new { id = "d", domain = "acme.test" }));
        var domain = await client.Domains.RetrieveAsync("d");
        Assert.Equal("acme.test", domain.Domain);
    }

    private sealed class SlowStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
