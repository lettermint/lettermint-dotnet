using System.Net;
using System.Text.Json;
using Lettermint.Models;
using Xunit;

namespace Lettermint.Tests;

public class SchedulingTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
    }

    [Fact]
    public async Task SendsSchedulingTagsTlsAndBatchMaps()
    {
        int calls = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var payload = calls++ == 0 ? body.RootElement : body.RootElement[0];
            Assert.Equal("tomorrow at 9am", payload.GetProperty("scheduled_at").GetString());
            Assert.Equal("campaign", payload.GetProperty("tags")[0].GetProperty("name").GetString());
            Assert.Equal("enforced", payload.GetProperty("settings").GetProperty("tls").GetString());
            if (calls == 2)
            {
                Assert.Equal("value", payload.GetProperty("headers").GetProperty("X-Test").GetString());
                Assert.Equal("value", payload.GetProperty("metadata").GetProperty("custom").GetString());
            }
            return new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(calls == 1 ? "{\"message_id\":\"m\",\"status\":\"scheduled\",\"scheduled_at\":\"2026-09-08T09:00:00Z\"}" : "[]")
            };
        }));
        using var email = LettermintClient.Email("project-token", new ClientOptions { HttpClient = http });
        var response = await email.From("a@example.com").To("b@example.com").Subject("Scheduled email")
            .Text("Hello there").ScheduledAt("tomorrow at 9am")
            .Tags(new MessageTag("campaign", "welcome"))
            .Settings(new() { Tls = TlsPolicy.Enforced }).SendAsync();
        Assert.Equal(MessageStatus.Scheduled, response.Status);
        await email.SendBatchAsync(new List<SendBatchMailRequestItem>
        {
            new()
            {
                ScheduledAt = "tomorrow at 9am", Tags = [new() { Name = "campaign", Value = "welcome" }],
                Settings = new() { Tls = TlsPolicy.Enforced },
                Headers = new() { ["X-Test"] = "value" }, Metadata = new() { ["custom"] = "value" }
            }
        });
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ValidatesTypedMessageTagsAndKeepsLegacyInput()
    {
        Assert.Throws<ArgumentException>(() => new MessageTag("invalid name", "value"));
        Assert.Throws<ArgumentException>(() => new MessageTag("__LETTERMINT_internal", "value"));

        using var email = LettermintClient.Email("project-token");
        var builder = email.From("a@example.com").To("b@example.com").Subject("Tags").Text("body");
        Assert.Throws<ArgumentException>(() => builder.Tags(
            new MessageTag("same", "one"), new MessageTag("same", "two")));
        Assert.Throws<ArgumentException>(() => builder.Tag("legacy").Tags(
            Enumerable.Range(0, 20).Select(index => new MessageTag($"tag_{index}", "value")).ToArray()));

        builder.Tags(new SendMailRequestTagsItem { Name = "legacy_map", Value = "value" });
    }

    [Fact]
    public async Task ProjectTokensCanRescheduleAndCancel()
    {
        int calls = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal("project-token", request.Headers.GetValues("x-lettermint-token").Single());
            Assert.False(request.Headers.Contains("Authorization"));
            if (calls++ == 0)
            {
                Assert.Equal(HttpMethod.Patch, request.Method);
                Assert.Equal("/v1/messages/message-id", request.RequestUri!.AbsolutePath);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal("tomorrow", body.RootElement.GetProperty("scheduled_at").GetString());
            }
            else
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/v1/messages/message-id/cancel", request.RequestUri!.AbsolutePath);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"message_id\":\"message-id\",\"status\":\"canceled\",\"scheduled_at\":null}") };
        }));
        using var email = LettermintClient.Email("project-token", new ClientOptions { HttpClient = http });
        await email.RescheduleAsync("message-id", new() { ScheduledAt = "tomorrow" });
        var canceled = await email.CancelAsync("message-id");
        Assert.Equal(MessageStatus.Canceled, canceled.Status);
        Assert.Null(canceled.ScheduledAt);
    }
}
