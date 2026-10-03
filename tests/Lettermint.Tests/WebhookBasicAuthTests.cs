using System.Net;
using System.Text;
using System.Text.Json;
using Lettermint.Models;
using Xunit;

namespace Lettermint.Tests;

public class WebhookBasicAuthTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CredentialStatesKeepBearerAuthentication(int state)
    {
        OptionalNullable<WebhookBasicAuthData> auth = state switch
        {
            1 => new WebhookBasicAuthData { Username = " fixture user ", Password = "" },
            2 => OptionalNullable<WebhookBasicAuthData>.Null,
            _ => default
        };
        using var handler = new Handler(state);
        using var http = new HttpClient(handler);
        using var api = LettermintClient.Api("fixture-token", new() { HttpClient = http, BaseUrl = new("https://example.test/v1") });
        var created = await api.Webhooks.CreateAsync(new StoreWebhookData { Name = "Fixture", Url = "https://example.test/hook", BasicAuth = auth });
        Assert.True(created.Data!.HasBasicAuth);
        var updated = await api.Webhooks.UpdateAsync("webhook-id", new UpdateWebhookData { BasicAuth = auth });
        Assert.True(updated.Data!.HasBasicAuth);
        Assert.Equal(2, handler.Calls);

        var decoded = JsonSerializer.Deserialize<UpdateWebhookData>(new UpdateWebhookData { BasicAuth = auth }.ToJson(), LettermintJson.Options)!;
        Assert.Equal(state != 0, decoded.BasicAuth.IsSet);
        if (state == 1) Assert.Equal("", decoded.BasicAuth.Value!.Password);
        if (state == 2) Assert.Null(decoded.BasicAuth.Value);
    }

    [Fact]
    public void ReadModelsExposeTheSafeFlag()
    {
        Assert.True(JsonSerializer.Deserialize<WebhookData>("{\"has_basic_auth\":true}", LettermintJson.Options)!.HasBasicAuth);
        Assert.True(JsonSerializer.Deserialize<WebhookListData>("{\"has_basic_auth\":true}", LettermintJson.Options)!.HasBasicAuth);
        Assert.True(JsonSerializer.Deserialize<WebhookSecretData>("{\"has_basic_auth\":true}", LettermintJson.Options)!.HasBasicAuth);
    }

    private sealed class Handler(int state) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("Bearer fixture-token", request.Headers.Authorization!.ToString());
            Assert.False(request.Headers.Contains("x-lettermint-token"));
            Assert.Equal(Calls == 1 ? HttpMethod.Post : HttpMethod.Put, request.Method);
            Assert.Equal(Calls == 1 ? "/v1/webhooks" : "/v1/webhooks/webhook-id", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal(state != 0, body.RootElement.TryGetProperty("basic_auth", out var auth));
            if (state == 1) { Assert.Equal("", auth.GetProperty("password").GetString()); Assert.Equal(" fixture user ", auth.GetProperty("username").GetString()); }
            if (state == 2) Assert.Equal(JsonValueKind.Null, auth.ValueKind);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":{\"has_basic_auth\":true}}", Encoding.UTF8, "application/json") };
        }
    }
}
