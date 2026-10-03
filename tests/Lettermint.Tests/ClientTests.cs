using System.Text.Json;
using Lettermint.Models;

namespace Lettermint.Tests;

public class ClientTests
{
    [Fact]
    public void RequiresAtLeastOneToken()
    {
        var error = Assert.Throws<LettermintConfigException>(() => new LettermintClient(new LettermintOptions()));
        Assert.Equal("Pass SendingToken, TeamToken or both in LettermintOptions.", error.Message);
        Assert.Throws<LettermintConfigException>(() => new LettermintClient((LettermintOptions)null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("lm_with space")]
    [InlineData("lm_tab\tbed")]
    [InlineData("lm_newline\nvalue")]
    [InlineData("lm_ünïcode")]
    public void RejectsTokensThatAreNotHeaderSafe(string token)
    {
        var error = Assert.Throws<LettermintConfigException>(() => new LettermintClient(new LettermintOptions { SendingToken = token }));
        Assert.StartsWith("SendingToken ", error.Message);
        if (token.Length > 0)
        {
            Assert.DoesNotContain(token, error.Message);
        }
        Assert.Throws<LettermintConfigException>(() => new LettermintClient(new LettermintOptions { TeamToken = token }));
    }

    [Theory]
    [InlineData("lm_team_Conformance0Token1Fake2Value3Only4Test5D", "team")]
    [InlineData("lm_Proj32Conformance0Token1Fake2Val", "sending")]
    [InlineData("lm_Proj22Conformance0Toke", "sending")]
    [InlineData("lm_team_", "config")]
    [InlineData("lm_sso_SsoConformance0Token1Fake2Value3", "config")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJjb25mb3JtYW5jZSJ9.Y29uZm9ybWFuY2Utc2lnbmF0dXJl", "config")]
    [InlineData("sk_conformance_0123456789abcdef", "config")]
    [InlineData("", "config")]
    [InlineData("lm_abc\n", "config")]
    [InlineData("lm_", "config")]
    public async Task DetectsTheTokenTypeOfAShorthandToken(string token, string expected)
    {
        var handler = new FakeHandler((_, _, _) => Task.FromResult(Fake.Text(200, "pong")));
        var options = new LettermintOptions { HttpClient = new HttpClient(handler) };
        if (expected == "config")
        {
            var error = Assert.Throws<LettermintConfigException>(() => new LettermintClient(token, options));
            Assert.Equal("Unrecognised token format; pass LettermintOptions.SendingToken or LettermintOptions.TeamToken instead.", error.Message);
            Assert.Empty(handler.Requests);
            return;
        }
        using var client = new LettermintClient(token, options);
        Assert.Equal("pong", await client.PingAsync());
        var request = Assert.Single(handler.Requests);
        if (expected == "team")
        {
            Assert.Equal("Bearer " + token, request.Header("Authorization"));
            Assert.Null(request.Header("x-lettermint-token"));
        }
        else
        {
            Assert.Equal(token, request.Header("x-lettermint-token"));
            Assert.Null(request.Header("Authorization"));
        }
    }

    [Fact]
    public void ShorthandRejectsTokensInTheOptionsToo()
    {
        var error = Assert.Throws<LettermintConfigException>(() => new LettermintClient(Fake.SendingToken, new LettermintOptions { TeamToken = Fake.TeamToken }));
        Assert.DoesNotContain(Fake.SendingToken, error.Message);
        Assert.DoesNotContain(Fake.TeamToken, error.Message);
    }

    [Theory]
    [InlineData("ftp://api.example.test/v1")]
    [InlineData("https://user:pass@api.example.test/v1")]
    [InlineData("https://api.example.test/v1?x=1")]
    [InlineData("https://api.example.test/v1#x")]
    public void RejectsInvalidBaseUrls(string url)
    {
        Assert.Throws<LettermintConfigException>(() => new LettermintClient(new LettermintOptions { SendingToken = Fake.SendingToken, BaseUrl = new Uri(url) }));
    }

    [Fact]
    public void RejectsARelativeBaseUrlAndInvalidTimeouts()
    {
        Assert.Throws<LettermintConfigException>(() => new LettermintClient(new LettermintOptions { SendingToken = Fake.SendingToken, BaseUrl = new Uri("/v1", UriKind.Relative) }));
        Assert.Throws<LettermintConfigException>(() => new LettermintClient(new LettermintOptions { SendingToken = Fake.SendingToken, Timeout = TimeSpan.Zero }));
        Assert.Throws<LettermintConfigException>(() => new LettermintClient(new LettermintOptions { SendingToken = Fake.SendingToken, Timeout = TimeSpan.FromSeconds(-1) }));
    }

    [Fact]
    public async Task StripsATrailingSlashFromTheBaseUrl()
    {
        var handler = new FakeHandler((_, _, _) => Task.FromResult(Fake.Text(200, "pong")));
        using var client = new LettermintClient(new LettermintOptions { TeamToken = Fake.TeamToken, BaseUrl = new Uri("http://localhost:8080/api/v1/"), HttpClient = new HttpClient(handler) });
        Assert.Equal("http://localhost:8080/api/v1", client.BaseUrl);
        await client.PingAsync();
        Assert.Equal("http://localhost:8080/api/v1/ping", handler.Requests[0].Url.ToString());
    }

    [Fact]
    public void RejectsAnHttpClientWithDefaultAuthenticationHeaders()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Add("x-lettermint-token", "other");
        Assert.Throws<LettermintConfigException>(() => new LettermintClient(new LettermintOptions { SendingToken = Fake.SendingToken, HttpClient = http }));
        var bearer = new HttpClient();
        bearer.DefaultRequestHeaders.Authorization = new("Bearer", "x");
        Assert.Throws<LettermintConfigException>(() => new LettermintClient(new LettermintOptions { TeamToken = Fake.TeamToken, HttpClient = bearer }));
    }

    [Fact]
    public async Task EachPartUsesItsOwnTokenAndNeverFallsBack()
    {
        var (sendingOnly, sendingHandler) = Fake.Client(team: false);
        var error = await Assert.ThrowsAsync<LettermintConfigException>(() => sendingOnly.Domains.ListAsync());
        Assert.Equal("Domains.ListAsync needs TeamToken; set LettermintOptions.TeamToken.", error.Message);
        await Assert.ThrowsAsync<LettermintConfigException>(() => sendingOnly.Webhooks.Deliveries.RetrieveAsync("w", "d"));
        await Assert.ThrowsAsync<LettermintConfigException>(() => sendingOnly.BlockedFileTypesAsync());
        Assert.Empty(sendingHandler.Requests);

        var (teamOnly, teamHandler) = Fake.Client(sending: false);
        var sendError = await Assert.ThrowsAsync<LettermintConfigException>(() => teamOnly.Emails.SendAsync(Fake.Message()));
        Assert.Equal("Emails.SendAsync needs SendingToken; set LettermintOptions.SendingToken.", sendError.Message);
        Assert.Throws<LettermintConfigException>(() => teamOnly.Emails.Compose());
        await Assert.ThrowsAsync<LettermintConfigException>(() => teamOnly.Emails.PingAsync());
        Assert.Empty(teamHandler.Requests);
    }

    [Fact]
    public async Task PingRescheduleAndCancelPreferTheTeamTokenAndFallBackToTheSendingToken()
    {
        var scheduled = new { message_id = "m", status = "scheduled", scheduled_at = "2026-10-20T09:00:00Z" };
        var (both, bothHandler) = Fake.Client((request, _) => request.Url.AbsolutePath.EndsWith("/ping") ? Fake.Text(200, "pong\n") : Fake.Json(200, scheduled));
        Assert.Equal("pong", await both.PingAsync());
        await both.Messages.RescheduleAsync("m", new RescheduleMessageRequest { ScheduledAt = "tomorrow" });
        await both.Messages.CancelAsync("m");
        Assert.All(bothHandler.Requests, request => Assert.Equal("Bearer " + Fake.TeamToken, request.Header("Authorization")));
        Assert.All(bothHandler.Requests, request => Assert.Null(request.Header("x-lettermint-token")));

        var (sendingOnly, sendingHandler) = Fake.Client((request, _) => request.Url.AbsolutePath.EndsWith("/ping") ? Fake.Text(200, "pong") : Fake.Json(200, scheduled), team: false);
        await sendingOnly.PingAsync();
        var result = await sendingOnly.Messages.CancelAsync("m");
        Assert.Equal(MessageStatus.Scheduled, result.Status);
        await sendingOnly.Messages.RescheduleAsync("m", new RescheduleMessageRequest { ScheduledAt = "tomorrow" });
        Assert.All(sendingHandler.Requests, request => Assert.Equal(Fake.SendingToken, request.Header("x-lettermint-token")));
        Assert.All(sendingHandler.Requests, request => Assert.Null(request.Header("Authorization")));

        var (_, emailsHandler) = Fake.Client((_, _) => Fake.Text(200, "pong"));
        var (client, handler) = Fake.Client((_, _) => Fake.Text(200, "pong"));
        await client.Emails.PingAsync();
        Assert.Equal(Fake.SendingToken, handler.Requests[0].Header("x-lettermint-token"));
        Assert.Empty(emailsHandler.Requests);
    }

    [Fact]
    public async Task NoOutputOfAnyObjectContainsATokenOrSecret()
    {
        var (client, _) = Fake.Client((_, _) => Fake.Json(500, new { message = "Server Error" }));
        var options = new LettermintOptions { SendingToken = Fake.SendingToken, TeamToken = Fake.TeamToken };
        var builder = client.Emails.Compose().From("a@example.test").To("b@example.test").Subject("Hi");
        var error = await Assert.ThrowsAsync<ServerException>(() => builder.SendAsync(new IdempotentRequestOptions { IdempotencyKey = "k" }));
        var webhook = new Webhook("whsec_secretForTheRedactionTest");
        var webhookError = Assert.Throws<WebhookVerificationException>(() => webhook.VerifySignature("{}", "t=1,v1=" + new string('0', 64), "1"));
        object[] subjects =
        [
            client, client.Emails, client.Domains, client.Messages, client.Projects, client.Projects.ReportForwarding, client.Routes,
            client.Stats, client.Suppressions, client.Team, client.Team.Members, client.Webhooks, client.Webhooks.Deliveries,
            options, builder, error, webhook, webhookError,
        ];
        foreach (var subject in subjects)
        {
            var outputs = new List<string> { subject.ToString()!, $"{subject}" };
            try
            {
                outputs.Add(JsonSerializer.Serialize(subject, subject.GetType()));
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or JsonException)
            {
                // Some types cannot be serialized at all, which leaks nothing either.
            }
            foreach (var output in outputs)
            {
                Assert.DoesNotContain(Fake.SendingToken, output);
                Assert.DoesNotContain(Fake.TeamToken, output);
                Assert.DoesNotContain("whsec_secretForTheRedactionTest", output);
            }
        }
        Assert.Contains("SendingToken = [redacted]", client.ToString());
        Assert.Contains("TeamToken = [redacted]", options.ToString());
        Assert.Equal("Domains", client.Domains.ToString());
    }

    [Theory]
    [InlineData(typeof(Internal.Transport), "_sendingToken")]
    [InlineData(typeof(Internal.Transport), "_teamToken")]
    [InlineData(typeof(LettermintClient), "_transport")]
    [InlineData(typeof(LettermintOptions), "SendingToken")]
    [InlineData(typeof(LettermintOptions), "TeamToken")]
    [InlineData(typeof(Webhook), "_key")]
    public void SecretsAreHiddenFromTheDebugger(Type type, string member)
    {
        var found = type.GetMember(member, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var attribute = Assert.Single(found).GetCustomAttributes(typeof(System.Diagnostics.DebuggerBrowsableAttribute), false);
        Assert.Equal(System.Diagnostics.DebuggerBrowsableState.Never, ((System.Diagnostics.DebuggerBrowsableAttribute)Assert.Single(attribute)).State);
    }

    [Fact]
    public async Task DisposesOnlyAnHttpClientItCreated()
    {
        var handler = new FakeHandler((_, _, _) => Task.FromResult(Fake.Text(200, "pong")));
        var http = new HttpClient(handler);
        var client = new LettermintClient(new LettermintOptions { TeamToken = Fake.TeamToken, HttpClient = http });
        client.Dispose();
        Assert.False(handler.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.PingAsync());
        await new LettermintClient(new LettermintOptions { TeamToken = Fake.TeamToken, HttpClient = http }).PingAsync();
    }

    [Fact]
    public async Task SendsTheUserAgentAndAccept()
    {
        var (client, handler) = Fake.Client((_, _) => Fake.Text(200, "pong"));
        await client.PingAsync();
        Assert.StartsWith("lettermint-dotnet/", handler.Requests[0].Header("User-Agent"));
        Assert.Equal("application/json", handler.Requests[0].Header("Accept"));
    }
}
