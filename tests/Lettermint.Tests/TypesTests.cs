using System.Text.Json;
using Lettermint.Internal;
using Lettermint.Models;

namespace Lettermint.Tests;

public class TypesTests
{
    [Fact]
    public void OpenEnumsDecodeUnknownValuesAndKeepThem()
    {
        var status = JsonSerializer.Deserialize<MessageStatus>("\"some_future_status\"", LettermintJson.Options);
        Assert.Equal("some_future_status", status.ToString());
        Assert.False(status.IsKnown);
        Assert.Equal("\"some_future_status\"", JsonSerializer.Serialize(status, LettermintJson.Options));
        Assert.Equal(new MessageStatus("some_future_status"), status);
        Assert.True(status == "some_future_status");

        var known = JsonSerializer.Deserialize<MessageStatus>("\"delivered\"", LettermintJson.Options);
        Assert.Equal(MessageStatus.Delivered, known);
        Assert.True(known.IsKnown);
        Assert.Contains(MessageStatus.Delivered, MessageStatus.KnownValues);
        Assert.NotEqual(MessageStatus.Delivered, new MessageStatus("Delivered"));
        Assert.Equal(MessageStatus.Delivered.GetHashCode(), new MessageStatus("delivered").GetHashCode());
        Assert.Equal(string.Empty, default(MessageStatus).ToString());
        Assert.Throws<ArgumentNullException>(() => new MessageStatus(null!));

        var nullable = JsonSerializer.Deserialize<SendMailResponse>("""{"message_id":"m","status":42}""", LettermintJson.Options)!;
        Assert.Equal("42", nullable.Status.ToString());
    }

    [Fact]
    public void OpenEnumsWorkInSwitchExpressions()
    {
        static string Describe(MessageStatus status) => status.ToString() switch
        {
            "delivered" => "ok",
            _ when status == MessageStatus.HardBounced => "bounced",
            _ => "other",
        };
        Assert.Equal("ok", Describe(MessageStatus.Delivered));
        Assert.Equal("bounced", Describe(MessageStatus.HardBounced));
        Assert.Equal("other", Describe("brand_new"));
    }

    [Fact]
    public void StringUnionsAreOpenEnumsWithConversionsFromTheirVariants()
    {
        AnalyticsDimension catalogue = AnalyticsCatalogueDimension.ProjectId;
        AnalyticsDimension tag = "tag:campaign";
        Assert.Equal("project_id", catalogue.ToString());
        Assert.Equal("tag:campaign", tag.ToString());
        Assert.True(catalogue.IsKnown);
        Assert.False(tag.IsKnown);
    }

    [Fact]
    public void SortEnumsNameDescendingValues()
    {
        Assert.Equal("-created_at", ListDomainsQuerySortItem.CreatedAtDescending.ToString());
        Assert.Equal("created_at", ListDomainsQuerySortItem.CreatedAt.ToString());
    }

    [Fact]
    public void OptionalFieldsDistinguishUnsetFromNull()
    {
        Assert.Equal("{}", JsonSerializer.Serialize(new UpdateRouteSettingsData(), LettermintJson.Options));
        Assert.Equal("""{"track_opens":null}""", JsonSerializer.Serialize(new UpdateRouteSettingsData { TrackOpens = null }, LettermintJson.Options));
        Assert.Equal("""{"track_opens":true}""", JsonSerializer.Serialize(new UpdateRouteSettingsData { TrackOpens = true }, LettermintJson.Options));
        Assert.Equal("""{"track_opens":false}""", JsonSerializer.Serialize(new UpdateRouteSettingsData { TrackOpens = false }, LettermintJson.Options));

        var unset = JsonSerializer.Deserialize<UpdateRouteSettingsData>("{}", LettermintJson.Options)!;
        Assert.False(unset.TrackOpens.IsSet);
        var setNull = JsonSerializer.Deserialize<UpdateRouteSettingsData>("""{"track_opens":null}""", LettermintJson.Options)!;
        Assert.True(setNull.TrackOpens.IsSet);
        Assert.Null(setNull.TrackOpens.Value);
        Assert.Equal(Optional<bool?>.Unset, default);
        Assert.NotEqual(Optional<bool?>.Unset, new Optional<bool?>(null));
        Assert.Equal("(unset)", Optional<string?>.Unset.ToString());
        Assert.Equal("null", new Optional<string?>(null).ToString());
    }

    [Fact]
    public void RequiredNullableFieldsAreAlwaysSentAndOptionalFieldsAreOmitted()
    {
        var json = JsonSerializer.Serialize(new SendMailRequest { From = "a", To = ["b"], Subject = "s" }, LettermintJson.Options);
        Assert.Equal("""{"from":"a","to":["b"],"subject":"s"}""", json);
    }

    [Fact]
    public void ResponseFieldsReadAbsentAndNullAsNull()
    {
        var route = JsonSerializer.Deserialize<RouteData>("""{"id":"r","inbound_domain":null}""", LettermintJson.Options)!;
        Assert.Null(route.InboundDomain);
        Assert.Null(route.InboundAddress);
    }

    [Fact]
    public void MapsAcceptTheEmptyArrayPhpSendsForAnEmptyMap()
    {
        var withArray = JsonSerializer.Deserialize<MessageData>("""{"id":"m","metadata":[]}""", LettermintJson.Options)!;
        Assert.Empty(withArray.Metadata!);
        var withObject = JsonSerializer.Deserialize<MessageData>("""{"id":"m","metadata":{"a":"1"}}""", LettermintJson.Options)!;
        Assert.Equal("1", withObject.Metadata!["a"]);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MessageData>("""{"id":"m","metadata":[1]}""", LettermintJson.Options));
    }

    [Fact]
    public void CursorPagesShareOneGenericType()
    {
        var page = JsonSerializer.Deserialize<ListDomainsResponse>("""{"data":[{"id":"d1"}],"path":null,"per_page":30,"next_cursor":"c","next_page_url":null,"prev_cursor":null,"prev_page_url":null}""", LettermintJson.Options)!;
        CursorPage<DomainListData> generic = page;
        Assert.Equal("d1", generic.Data[0].Id);
        Assert.Equal("c", generic.NextCursor);
        Assert.Equal(30, generic.PerPage);
    }

    [Fact]
    public void ToStringRedactsSecretsInResponses()
    {
        var created = JsonSerializer.Deserialize<ProjectCreatedData>("""{"data":{"id":"p","name":"Production"},"message":"Project created","api_token":"lm_secretProjectToken123"}""", LettermintJson.Options)!;
        Assert.DoesNotContain("lm_secretProjectToken123", created.ToString());
        Assert.Contains("ApiToken = [redacted]", created.ToString());
        Assert.Contains("Message = Project created", created.ToString());
        Assert.Contains("Name = Production", created.ToString());
        Assert.Equal("lm_secretProjectToken123", created.ApiToken);
        var secret = new WebhookBasicAuthData { Username = "user", Password = "hunter2" };
        Assert.DoesNotContain("hunter2", secret.ToString());
        Assert.DoesNotContain("hunter2", new UpdateWebhookData { BasicAuth = secret }.ToString());
    }

    [Fact]
    public void UnknownFieldsAreKept()
    {
        var domain = JsonSerializer.Deserialize<DomainData>("""{"id":"d","brand_new":{"a":[1]}}""", LettermintJson.Options)!;
        Assert.Equal("""{"a":[1]}""", domain.AdditionalProperties!["brand_new"].GetRawText());
        Assert.Contains("\"brand_new\":{\"a\":[1]}", JsonSerializer.Serialize(domain, LettermintJson.Options));
    }
}
