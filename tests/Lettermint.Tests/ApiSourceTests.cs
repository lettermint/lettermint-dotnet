using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lettermint.Models;
using Xunit;

namespace Lettermint.Tests;

public class ApiSourceTests
{
    private static JsonElement Fixtures => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "api-source.json"))).RootElement;
    public static IEnumerable<object[]> SourceModels => Fixtures.GetProperty("models").EnumerateObject().Select(p => new object[] { p.Name, p.Value.GetRawText() });
    public static IEnumerable<object[]> SourceEnums => Fixtures.GetProperty("enums").EnumerateObject().Select(p => new object[] { p.Name, p.Value.GetRawText() });

    [Theory]
    [MemberData(nameof(SourceModels))]
    public void DeserializesActualApiDataClassOutput(string name, string json)
    {
        var type = typeof(MessageData).Assembly.GetType("Lettermint.Models." + name, throwOnError: true)!;
        var model = Assert.IsAssignableFrom<ApiModel>(JsonSerializer.Deserialize(json, type, LettermintJson.Options));
        // Every field emitted by the API must have a named property, not just an escape hatch.
        Assert.True(model.AdditionalProperties == null || model.AdditionalProperties.Count == 0,
            name + " has untyped fields: " + string.Join(", ", model.AdditionalProperties?.Keys ?? Enumerable.Empty<string>()));
    }

    [Theory]
    [MemberData(nameof(SourceEnums))]
    public void AcceptsEveryActualApiEnumValue(string name, string values)
    {
        var type = typeof(MessageStatus).Assembly.GetType("Lettermint.Models." + name, throwOnError: true)!;
        foreach (var value in JsonDocument.Parse(values).RootElement.EnumerateArray())
        {
            var parsed = JsonSerializer.Deserialize(value.GetRawText(), type, LettermintJson.Options);
            Assert.Equal(value.GetRawText(), JsonSerializer.Serialize(parsed, type, LettermintJson.Options));
        }
    }

    [Fact]
    public void ParsesImmediateAndScheduledControllerResponses()
    {
        var fixture = Fixtures.GetProperty("SendMailController");
        var scheduled = fixture.GetProperty("scheduled").Deserialize<SendEmailResponse>(LettermintJson.Options)!;
        Assert.Equal(MessageStatus.Scheduled, scheduled.Status);
        Assert.Equal("2026-09-08T12:00:00+00:00", scheduled.ScheduledAt);
        var immediate = fixture.GetProperty("immediate").Deserialize<SendEmailResponse>(LettermintJson.Options)!;
        Assert.Equal(MessageStatus.Pending, immediate.Status);
        Assert.Null(immediate.ScheduledAt);
        var batch = Fixtures.GetProperty("SendBatchMailController").GetProperty("scheduled").Deserialize<SendBatchEmailResponseItem>(LettermintJson.Options)!;
        Assert.Equal(MessageStatus.Scheduled, batch.Status);
        Assert.Equal(scheduled.ScheduledAt, batch.ScheduledAt);
    }

    [Fact]
    public void ParsesBothSuppressionRemovalOutcomes()
    {
        var removed = Fixtures.GetProperty("suppression_removed").Deserialize<SuppressionDestroyResponse>(LettermintJson.Options)!;
        Assert.Equal("removed", removed.Status);
        var review = Fixtures.GetProperty("suppression_review").Deserialize<SuppressionDestroyResponse>(LettermintJson.Options)!;
        Assert.Equal("review_ticket_created", review.Status);
        Assert.Equal("SUP-123", review.TicketIdentifier);
        Assert.Equal(0.75, review.Confidence);
    }

    [Fact]
    public void ParsesActualRouteSettingsAndServerGeneratedSignature()
    {
        var route = Fixtures.GetProperty("route_settings").Deserialize<RouteData>(LettermintJson.Options)!;
        Assert.Equal(AttachmentDelivery.Url, route.Settings!.AttachmentDelivery);
        Assert.Equal(TlsPolicy.Enforced, route.Settings.Tls);
        var signature = Fixtures.GetProperty("webhook_signature");
        var result = Webhook.Verify(signature.GetProperty("body").GetString()!, signature.GetProperty("signature").GetString()!,
            "fixture-signing-secret", timeProvider: new FixtureClock());
        Assert.Equal("Café", result.GetProperty("name").GetString());
    }

    public static IEnumerable<object[]> SourcePages => Fixtures.GetProperty("pages").EnumerateObject().Select(p => new object[] { p.Name, p.Value.GetRawText() });

    [Theory]
    [MemberData(nameof(SourcePages))]
    public void ParsesActualLaravelPaginatorOutput(string name, string json)
    {
        var type = typeof(MessageData).Assembly.GetType("Lettermint.Models." + name, throwOnError: true)!;
        var model = Assert.IsAssignableFrom<ApiModel>(JsonSerializer.Deserialize(json, type, LettermintJson.Options));
        Assert.True(model.AdditionalProperties == null || model.AdditionalProperties.Count == 0);
        Assert.NotNull(type.GetProperty("NextCursor")!.GetValue(model));
        Assert.NotNull(type.GetProperty("NextPageUrl")!.GetValue(model));
        Assert.Single((System.Collections.IEnumerable)type.GetProperty("Data")!.GetValue(model)!);
    }

    [Fact]
    public void RequestModelsCoverActualPhpValidationFields()
    {
        foreach (var (key, rootType) in new[] { ("single", typeof(SendMailRequest)), ("batch", typeof(SendBatchMailRequestItem)) })
        {
            foreach (var field in Fixtures.GetProperty("request_rules").GetProperty(key).EnumerateArray())
            {
                var path = field.GetString()!;
                if (key == "batch") path = path.StartsWith("*.") ? path[2..] : path;
                if (path == "*") continue;
                var type = rootType;
                foreach (var segment in path.Split('.'))
                {
                    type = Nullable.GetUnderlyingType(type) ?? type;
                    if (segment == "*")
                    {
                        Assert.True(type.IsGenericType, path);
                        type = type.GetGenericArguments().Last();
                        continue;
                    }
                    var property = type.GetProperties().SingleOrDefault(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == segment);
                    Assert.True(property != null, key + ": missing typed validation field " + path);
                    type = property!.PropertyType;
                }
            }
        }
    }

    [Fact]
    public void HandlesPhpEmptyMapsButRejectsNonemptyArrays()
    {
        Assert.Empty(JsonSerializer.Deserialize<MessageData>("{\"metadata\":[]}", LettermintJson.Options)!.Metadata!);
        var value = JsonSerializer.Deserialize<MessageData>("{\"metadata\":{\"id\":\"123\"}}", LettermintJson.Options)!;
        Assert.Equal("123", value.Metadata!["id"]);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MessageData>("{\"metadata\":[\"invalid\"]}", LettermintJson.Options));
    }

    private sealed class FixtureClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(1788782400);
    }
}
