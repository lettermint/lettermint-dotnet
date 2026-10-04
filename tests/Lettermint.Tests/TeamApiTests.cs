using System.Net;
using System.Text.Json.Nodes;
using Lettermint.Internal;
using Lettermint.Models;

namespace Lettermint.Tests;

public class TeamApiTests
{
    private static readonly Dictionary<string, string> Ids = new()
    {
        ["domainId"] = "domain/1",
        ["recordId"] = "record 1",
        ["messageId"] = "message?1",
        ["projectId"] = "project#1",
        ["routeId"] = "route_1",
        ["suppressionId"] = "suppression_1",
        ["userId"] = "user/id",
        ["webhookId"] = "webhook_1",
        ["deliveryId"] = "delivery_1",
    };

    private static string Id(string name) => Ids[name];

    /// <summary>Every operation of the API, called through its public SDK method.</summary>
    private static readonly Dictionary<string, Func<LettermintClient, Task>> Calls = new()
    {
        ["DELETE /domains/{domainId}"] = l => l.Domains.DeleteAsync(Id("domainId")),
        ["DELETE /projects/{projectId}"] = l => l.Projects.DeleteAsync(Id("projectId")),
        ["DELETE /projects/{projectId}/report-forwarding"] = l => l.Projects.ReportForwarding.DeleteAsync(Id("projectId")),
        ["DELETE /routes/{routeId}"] = l => l.Routes.DeleteAsync(Id("routeId")),
        ["DELETE /suppressions/{suppressionId}"] = l => l.Suppressions.DeleteAsync(Id("suppressionId")),
        ["DELETE /webhooks/{webhookId}"] = l => l.Webhooks.DeleteAsync(Id("webhookId")),
        ["GET /blocked-file-types"] = l => l.BlockedFileTypesAsync(),
        ["GET /domains"] = l => l.Domains.ListAsync(),
        ["GET /domains/{domainId}"] = l => l.Domains.RetrieveAsync(Id("domainId")),
        ["GET /messages"] = l => l.Messages.ListAsync(),
        ["GET /messages/{messageId}"] = l => l.Messages.RetrieveAsync(Id("messageId")),
        ["GET /messages/{messageId}/events"] = l => l.Messages.EventsAsync(Id("messageId")),
        ["GET /messages/{messageId}/html"] = l => l.Messages.HtmlAsync(Id("messageId")),
        ["GET /messages/{messageId}/source"] = l => l.Messages.SourceAsync(Id("messageId")),
        ["GET /messages/{messageId}/text"] = l => l.Messages.TextAsync(Id("messageId")),
        ["GET /ping"] = l => l.PingAsync(),
        ["GET /projects"] = l => l.Projects.ListAsync(),
        ["GET /projects/{projectId}"] = l => l.Projects.RetrieveAsync(Id("projectId")),
        ["GET /projects/{projectId}/report-forwarding"] = l => l.Projects.ReportForwarding.RetrieveAsync(Id("projectId")),
        ["GET /projects/{projectId}/routes"] = l => l.Routes.ListAsync(Id("projectId")),
        ["GET /routes/{routeId}"] = l => l.Routes.RetrieveAsync(Id("routeId")),
        ["GET /stats"] = l => l.Stats.RetrieveAsync(new GetStatsQuery { From = "2026-01-01", To = "2026-01-31" }),
        ["GET /suppressions"] = l => l.Suppressions.ListAsync(),
        ["GET /team"] = l => l.Team.RetrieveAsync(),
        ["GET /team/members"] = l => l.Team.Members.ListAsync(),
        ["GET /team/members/{userId}"] = l => l.Team.Members.RetrieveAsync(Id("userId")),
        ["GET /team/roles"] = l => l.Team.RolesAsync(),
        ["GET /team/usage"] = l => l.Team.UsageAsync(),
        ["GET /webhooks"] = l => l.Webhooks.ListAsync(),
        ["GET /webhooks/{webhookId}"] = l => l.Webhooks.RetrieveAsync(Id("webhookId")),
        ["GET /webhooks/{webhookId}/deliveries"] = l => l.Webhooks.Deliveries.ListAsync(Id("webhookId")),
        ["GET /webhooks/{webhookId}/deliveries/{deliveryId}"] = l => l.Webhooks.Deliveries.RetrieveAsync(Id("webhookId"), Id("deliveryId")),
        ["PATCH /messages/{messageId}"] = l => l.Messages.RescheduleAsync(Id("messageId"), new RescheduleMessageRequest { ScheduledAt = "2026-10-01T09:00:00Z" }),
        ["POST /analytics"] = l => l.AnalyticsAsync(new AnalyticsQuery { Metrics = [AnalyticsMetric.Accepted] }),
        ["POST /domains"] = l => l.Domains.CreateAsync(new StoreDomainData { Domain = "example.test" }),
        ["POST /domains/{domainId}/dns-records/verify"] = l => l.Domains.VerifyDnsRecordsAsync(Id("domainId")),
        ["POST /domains/{domainId}/dns-records/{recordId}/verify"] = l => l.Domains.VerifyDnsRecordAsync(Id("domainId"), Id("recordId")),
        ["POST /messages/{messageId}/cancel"] = l => l.Messages.CancelAsync(Id("messageId")),
        ["POST /messages/{messageId}/process"] = l => l.Messages.ProcessAsync(Id("messageId")),
        ["POST /projects"] = l => l.Projects.CreateAsync(new StoreProjectData { Name = "Production" }),
        ["POST /projects/{projectId}/report-forwarding/resend-code"] = l => l.Projects.ReportForwarding.ResendCodeAsync(Id("projectId")),
        ["POST /projects/{projectId}/report-forwarding/verify"] = l => l.Projects.ReportForwarding.VerifyAsync(Id("projectId"), new VerifyReportForwardingRequest { Code = "123456" }),
        ["POST /projects/{projectId}/rotate-token"] = l => l.Projects.RotateTokenAsync(Id("projectId")),
        ["POST /projects/{projectId}/routes"] = l => l.Routes.CreateAsync(Id("projectId"), new StoreRouteData { Name = "Inbound", RouteType = RouteType.Inbound }),
        ["POST /routes/{routeId}/verify-inbound-domain"] = l => l.Routes.VerifyInboundDomainAsync(Id("routeId")),
        ["POST /send"] = l => l.Emails.SendAsync(Fake.Message()),
        ["POST /send/batch"] = l => l.Emails.SendBatchAsync([Fake.Message()]),
        ["POST /suppressions"] = l => l.Suppressions.CreateAsync(new StoreSuppressionData { Reason = SuppressionCreateReason.Manual, Scope = SuppressionCreateScope.Team, Emails = new[] { "blocked@example.test" } }),
        ["POST /webhooks"] = l => l.Webhooks.CreateAsync(new StoreWebhookData { Name = "Hook", Url = "https://example.test/hook", Events = [WebhookEvent.MessageSent] }),
        ["POST /webhooks/{webhookId}/regenerate-secret"] = l => l.Webhooks.RegenerateSecretAsync(Id("webhookId")),
        ["POST /webhooks/{webhookId}/test"] = l => l.Webhooks.TestAsync(Id("webhookId")),
        ["PUT /domains/{domainId}/projects"] = l => l.Domains.UpdateProjectsAsync(Id("domainId"), new UpdateDomainProjectsData { ProjectIds = ["p"] }),
        ["PUT /projects/{projectId}"] = l => l.Projects.UpdateAsync(Id("projectId"), new UpdateProjectData { Name = "Renamed" }),
        ["PUT /projects/{projectId}/report-forwarding"] = l => l.Projects.ReportForwarding.UpdateAsync(Id("projectId"), new ReportForwardingRequest { Destination = "reports@example.test" }),
        ["PUT /routes/{routeId}"] = l => l.Routes.UpdateAsync(Id("routeId"), new UpdateRouteData { Name = "Renamed" }),
        ["PUT /team"] = l => l.Team.UpdateAsync(new UpdateTeamData { Name = "Acme" }),
        ["PUT /team/members/{userId}/assignment"] = l => l.Team.Members.UpdateAssignmentAsync(Id("userId"), new UpdateTeamMemberAssignmentData { RoleId = "role_1", ProjectAccess = new UpdateTeamMemberAssignmentDataProjectAccess { Scope = ProjectAccessScope.All } }),
        ["PUT /webhooks/{webhookId}"] = l => l.Webhooks.UpdateAsync(Id("webhookId"), new UpdateWebhookData { BasicAuth = null }),
    };

    public static TheoryData<string> OperationKeys => new(Operations.All.Keys.Order());

    [Fact]
    public void HasAnSdkMethodForEveryOperationInTheGeneratedTable()
    {
        Assert.Equal(Operations.All.Keys.Order(), Calls.Keys.Order());
        Assert.Equal(58, Operations.All.Count);
    }

    private static HttpResponseMessage ResponseFor(OperationDefinition operation) => operation.ResponseKind switch
    {
        ResponseKind.Empty => new HttpResponseMessage(HttpStatusCode.NoContent),
        ResponseKind.Text => Fake.Text(200, "pong"),
        _ when operation.ResponseType.IsGenericType => Fake.Json(operation.ResponseStatus[0], Array.Empty<object>()),
        _ when operation.Pagination is not null => Fake.Json(operation.ResponseStatus[0], new { data = Array.Empty<object>(), next_cursor = (string?)null }),
        _ => Fake.Json(operation.ResponseStatus[0], new { }),
    };

    [Theory]
    [MemberData(nameof(OperationKeys))]
    public async Task CallsTheOperation(string key)
    {
        var operation = Operations.All[key];
        var (client, handler) = Fake.Client((_, _) => ResponseFor(operation));
        await Calls[key](client);
        var request = Assert.Single(handler.Requests);
        var path = operation.Path;
        foreach (var name in operation.PathParams)
        {
            path = path.Replace("{" + name + "}", Uri.EscapeDataString(Ids[name]));
        }
        Assert.Equal(operation.Method, request.Method);
        Assert.Equal("https://api.lettermint.co/v1" + path, request.Url.GetLeftPart(UriPartial.Path));
        if (operation.Auth == AuthSurface.Sending)
        {
            Assert.Equal(Fake.SendingToken, request.Header("x-lettermint-token"));
            Assert.Null(request.Header("Authorization"));
        }
        else
        {
            Assert.Equal("Bearer " + Fake.TeamToken, request.Header("Authorization"));
            Assert.Null(request.Header("x-lettermint-token"));
        }
        Assert.Equal("application/json", request.Header("Accept"));
        if (operation.RequestType is not null)
        {
            Assert.Equal("application/json", request.Header("Content-Type"));
            Assert.NotNull(request.Body);
        }
        else
        {
            Assert.Null(request.Body);
            Assert.Null(request.Header("Content-Type"));
        }
    }

    [Fact]
    public void FollowsTheSharedLayout()
    {
        var layout = new Dictionary<Type, string[]>
        {
            [typeof(LettermintClient)] = ["AnalyticsAsync", "BlockedFileTypesAsync", "PingAsync"],
            [typeof(Emails)] = ["Compose", "PingAsync", "SendAsync", "SendBatchAsync"],
            [typeof(Domains)] = ["CreateAsync", "DeleteAsync", "IterateAsync", "ListAsync", "RetrieveAsync", "UpdateProjectsAsync", "VerifyDnsRecordAsync", "VerifyDnsRecordsAsync"],
            [typeof(Messages)] = ["CancelAsync", "EventsAsync", "HtmlAsync", "IterateAsync", "IterateEventsAsync", "ListAsync", "ProcessAsync", "RescheduleAsync", "RetrieveAsync", "SourceAsync", "TextAsync"],
            [typeof(Projects)] = ["CreateAsync", "DeleteAsync", "IterateAsync", "ListAsync", "RetrieveAsync", "RotateTokenAsync", "UpdateAsync"],
            [typeof(ReportForwarding)] = ["DeleteAsync", "ResendCodeAsync", "RetrieveAsync", "UpdateAsync", "VerifyAsync"],
            [typeof(Routes)] = ["CreateAsync", "DeleteAsync", "IterateAsync", "ListAsync", "RetrieveAsync", "UpdateAsync", "VerifyInboundDomainAsync"],
            [typeof(Stats)] = ["RetrieveAsync"],
            [typeof(Suppressions)] = ["CreateAsync", "DeleteAsync", "IterateAsync", "ListAsync"],
            [typeof(Team)] = ["RetrieveAsync", "RolesAsync", "UpdateAsync", "UsageAsync"],
            [typeof(TeamMembers)] = ["IterateAsync", "ListAsync", "RetrieveAsync", "UpdateAssignmentAsync"],
            [typeof(Webhooks)] = ["CreateAsync", "DeleteAsync", "IterateAsync", "ListAsync", "RegenerateSecretAsync", "RetrieveAsync", "TestAsync", "UpdateAsync"],
            [typeof(WebhookDeliveries)] = ["IterateAsync", "ListAsync", "RetrieveAsync"],
        };
        foreach (var (type, expected) in layout)
        {
            var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName && method.Name is not ("ToString" or "Dispose"))
                .Select(method => method.Name)
                .Distinct()
                .Order();
            Assert.Equal(expected, methods);
        }
        Assert.Equal(typeof(ReportForwarding), typeof(Projects).GetProperty("ReportForwarding")!.PropertyType);
        Assert.Equal(typeof(TeamMembers), typeof(Team).GetProperty("Members")!.PropertyType);
        Assert.Equal(typeof(WebhookDeliveries), typeof(Webhooks).GetProperty("Deliveries")!.PropertyType);
    }

    [Fact]
    public async Task SendsRequestBodiesAsJson()
    {
        var (client, handler) = Fake.Client((_, _) => Fake.Json(200, new { }));
        await client.Team.Members.UpdateAssignmentAsync("user/id", new UpdateTeamMemberAssignmentData { RoleId = "role_123", ProjectAccess = new() { Scope = ProjectAccessScope.All } });
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"role_id":"role_123","project_access":{"scope":"all"}}"""), handler.Requests[0].Json), handler.Requests[0].Body);
        await client.Messages.RescheduleAsync("message/id", new RescheduleMessageRequest { ScheduledAt = "2026-08-27T09:00:00Z" });
        Assert.Equal("""{"scheduled_at":"2026-08-27T09:00:00Z"}""", handler.Requests[1].Body);
    }

    [Fact]
    public async Task SendsAnIdempotencyKeyForProcessingAnInboundMessage()
    {
        var (client, handler) = Fake.Client((_, _) => Fake.Json(202, new { data = new { message_id = "m" } }));
        await client.Messages.ProcessAsync("m", new IdempotentRequestOptions { IdempotencyKey = "process-1" });
        await client.Messages.ProcessAsync("m");
        Assert.Equal("process-1", handler.Requests[0].Header("Idempotency-Key"));
        Assert.Null(handler.Requests[1].Header("Idempotency-Key"));
    }

    [Fact]
    public async Task KeepsTheWebhookBasicAuthState()
    {
        var (client, handler) = Fake.Client((_, _) => Fake.Json(200, new { data = new { has_basic_auth = true } }));
        await client.Webhooks.UpdateAsync("w", new UpdateWebhookData());
        await client.Webhooks.UpdateAsync("w", new UpdateWebhookData { BasicAuth = new WebhookBasicAuthData { Username = " fixture user ", Password = "" } });
        await client.Webhooks.UpdateAsync("w", new UpdateWebhookData { BasicAuth = null });
        Assert.Equal("{}", handler.Requests[0].Body);
        Assert.Equal("""{"basic_auth":{"username":" fixture user ","password":""}}""", handler.Requests[1].Body);
        Assert.Equal("""{"basic_auth":null}""", handler.Requests[2].Body);
    }

    [Fact]
    public async Task SendsAnalyticsQueries()
    {
        var (client, handler) = Fake.Client((_, _) => Fake.Json(200, new
        {
            data = new { summary = new { metrics = new { accepted = 12, delivery_rate = (double?)null } } },
            meta = new { timezone = "UTC" },
        }));
        var result = await client.AnalyticsAsync(new AnalyticsQuery
        {
            Metrics = [AnalyticsMetric.Accepted, AnalyticsMetric.Delivered],
            Include = [AnalyticsSection.Summary, AnalyticsSection.TimeSeries],
            Interval = AnalyticsInterval.Day,
            GroupBy = [AnalyticsCatalogueGroupDimension.ProjectId, "tag:campaign"],
            Limit = 10,
        });
        Assert.Equal(12, result.Data.Summary!.Metrics.Accepted);
        Assert.Null(result.Data.Summary.Metrics.DeliveryRate);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""
            {"metrics":["accepted","delivered"],"include":["summary","time_series"],"interval":"day","group_by":["project_id","tag:campaign"],"limit":10}
            """), handler.Requests[0].Json), handler.Requests[0].Body);
    }

    [Theory]
    [InlineData("incoming.example.com")]
    [InlineData(null)]
    public async Task KeepsAnInboundRouteDomain(string? domain)
    {
        var route = new JsonObject
        {
            ["id"] = "route_1",
            ["project_id"] = "project_1",
            ["slug"] = "incoming",
            ["name"] = "Incoming",
            ["route_type"] = "inbound",
            ["is_default"] = false,
            ["created_at"] = "2026-10-01T12:00:00Z",
            ["updated_at"] = "2026-10-01T12:00:00Z",
            ["inbound_route_domain"] = domain,
        };
        var (client, _) = Fake.Client((_, _) => Fake.Text(200, route.ToJsonString(), "application/json"));
        var result = await client.Routes.RetrieveAsync("route_1");
        Assert.Equal(domain, result.InboundRouteDomain);
        Assert.Equal(RouteType.Inbound, result.RouteType);
    }

    [Fact]
    public async Task SerializesQueryObjectsToTheBracketSyntax()
    {
        var (client, handler) = Fake.Client();
        await client.Domains.ListAsync(new ListDomainsQuery
        {
            PageSize = 10,
            PageCursor = "abc",
            FilterStatus = DomainStatus.Verified,
            FilterDomain = "acme",
            Sort = [ListDomainsQuerySortItem.CreatedAtDescending, ListDomainsQuerySortItem.Domain],
        });
        Assert.Equal(
            new Dictionary<string, string> { ["page[size]"] = "10", ["page[cursor]"] = "abc", ["sort"] = "-created_at,domain", ["filter[status]"] = "verified", ["filter[domain]"] = "acme" },
            handler.Requests[0].QueryMap);
        Assert.Equal(5, handler.Requests[0].Query.Count);
    }

    [Fact]
    public async Task SerializesBooleansTagPairsAndSkipsNulls()
    {
        var (client, handler) = Fake.Client();
        await client.Messages.ListAsync(new ListMessagesQuery
        {
            Filter = new ListMessagesQueryFilter
            {
                Tags = [new() { Name = "campaign", Value = "welcome" }, new() { Name = "tier", Value = "gold" }],
            },
            FilterStatus = null,
            FilterSearch = "hello world",
        });
        Assert.Contains("filter%5Bsearch%5D=hello+world", handler.Requests[0].Url.AbsoluteUri);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["filter[search]"] = "hello world",
                ["filter[tags][0][name]"] = "campaign",
                ["filter[tags][0][value]"] = "welcome",
                ["filter[tags][1][name]"] = "tier",
                ["filter[tags][1][value]"] = "gold",
            },
            handler.Requests[0].QueryMap);
        await client.Webhooks.ListAsync(new ListWebhooksQuery { FilterEnabled = false, Cursor = "c1", PageSize = 5 });
        Assert.Equal(new Dictionary<string, string> { ["filter[enabled]"] = "0", ["cursor"] = "c1", ["page[size]"] = "5" }, handler.Requests[1].QueryMap);
        await client.Messages.EventsAsync("m", new ListMessageEventsQuery { IncludeMachineEvents = true, PageSize = 2 });
        Assert.Equal(new Dictionary<string, string> { ["include_machine_events"] = "1", ["page[size]"] = "2" }, handler.Requests[2].QueryMap);
        await client.Domains.RetrieveAsync("d", new GetDomainQuery { Include = [GetDomainQueryIncludeItem.DnsRecords, GetDomainQueryIncludeItem.Projects] });
        Assert.Equal(new Dictionary<string, string> { ["include"] = "dnsRecords,projects" }, handler.Requests[3].QueryMap);
        await client.Stats.RetrieveAsync(new GetStatsQuery { From = "2026-01-01", To = "2026-01-31", ProjectId = null });
        Assert.Equal(new Dictionary<string, string> { ["from"] = "2026-01-01", ["to"] = "2026-01-31" }, handler.Requests[4].QueryMap);
        Assert.Equal("https://api.lettermint.co/v1/stats?from=2026-01-01&to=2026-01-31", handler.Requests[4].Url.AbsoluteUri);
    }

    [Fact]
    public async Task FollowsNextCursorInPageCursorUntilItIsNull()
    {
        var pages = new Dictionary<string, object>
        {
            ["first"] = new { data = new[] { new { id = "d1" }, new { id = "d2" } }, next_cursor = "c2" },
            ["c2"] = new { data = new[] { new { id = "d3" } }, next_cursor = "c3" },
            ["c3"] = new { data = Array.Empty<object>(), next_cursor = (string?)null },
        };
        var (client, handler) = Fake.Client((request, _) => Fake.Json(200, pages[request.QueryMap.GetValueOrDefault("page[cursor]", "first")]));
        var ids = new List<string>();
        await foreach (var domain in client.Domains.IterateAsync(new ListDomainsQuery { PageSize = 2, FilterStatus = DomainStatus.Verified }))
        {
            ids.Add(domain.Id);
        }
        Assert.Equal(["d1", "d2", "d3"], ids);
        Assert.Equal(
            ["page[size]=2&filter[status]=verified", "page[size]=2&filter[status]=verified&page[cursor]=c2", "page[size]=2&filter[status]=verified&page[cursor]=c3"],
            handler.Requests.Select(r => string.Join("&", r.Query.Select(p => $"{p.Key}={p.Value}"))));
    }

    [Fact]
    public async Task UsesTheCursorParameterWhereTheTableSaysSo()
    {
        var (client, handler) = Fake.Client((request, _) => Fake.Json(200, request.QueryMap.ContainsKey("cursor")
            ? new { data = new[] { new { id = "w2" } }, next_cursor = (string?)null }
            : new { data = new[] { new { id = "w1" } }, next_cursor = (string?)"next" }));
        var ids = new List<string>();
        await foreach (var delivery in client.Webhooks.Deliveries.IterateAsync("hook/1"))
        {
            ids.Add(delivery.Id);
        }
        Assert.Equal(["w1", "w2"], ids);
        Assert.Equal("https://api.lettermint.co/v1/webhooks/hook%2F1/deliveries?cursor=next", handler.Requests[1].Url.AbsoluteUri);
        Assert.Equal("cursor", Operations.ListWebhookDeliveries.Definition.Pagination!.CursorParam);
    }

    public static TheoryData<string> Iterators => new("domains", "messages", "message events", "projects", "routes", "suppressions", "team members", "webhooks", "webhook deliveries");

    private static IAsyncEnumerable<object> Iterate(LettermintClient client, string name) => name switch
    {
        "domains" => client.Domains.IterateAsync(),
        "messages" => client.Messages.IterateAsync(),
        "message events" => client.Messages.IterateEventsAsync("m"),
        "projects" => client.Projects.IterateAsync(),
        "routes" => client.Routes.IterateAsync("p"),
        "suppressions" => client.Suppressions.IterateAsync(),
        "team members" => client.Team.Members.IterateAsync(),
        "webhooks" => client.Webhooks.IterateAsync(),
        _ => client.Webhooks.Deliveries.IterateAsync("w"),
    };

    [Theory]
    [MemberData(nameof(Iterators))]
    public async Task Iterates(string name)
    {
        var (client, handler) = Fake.Client((_, index) => Fake.Json(200, index == 0
            ? new { data = new[] { new { id = "1" } }, next_cursor = (string?)"n" }
            : new { data = new[] { new { id = "2" } }, next_cursor = (string?)null }));
        var items = new List<object>();
        await foreach (var item in Iterate(client, name))
        {
            items.Add(item);
        }
        Assert.Equal(2, items.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task StopsWhenTheApiRepeatsACursorAndWhenTheCallerBreaks()
    {
        var (client, handler) = Fake.Client((_, _) => Fake.Json(200, new { data = new[] { new { id = "x" } }, next_cursor = "same" }));
        var count = 0;
        await foreach (var _ in client.Projects.IterateAsync())
        {
            count++;
        }
        Assert.Equal(2, count);
        Assert.Equal(2, handler.Requests.Count);

        var (other, otherHandler) = Fake.Client((_, _) => Fake.Json(200, new { data = new[] { new { id = "x" }, new { id = "y" } }, next_cursor = "n" }));
        await foreach (var _ in other.Suppressions.IterateAsync())
        {
            break;
        }
        Assert.Single(otherHandler.Requests);
    }

    [Fact]
    public async Task EncodesPathParameters()
    {
        var (client, handler) = Fake.Client((_, _) => Fake.Json(200, new { }));
        await client.Domains.VerifyDnsRecordAsync("domain/../x", "record?a=b#c");
        Assert.Equal("https://api.lettermint.co/v1/domains/domain%2F..%2Fx/dns-records/record%3Fa%3Db%23c/verify", handler.Requests[0].Url.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(null)]
    public async Task RejectsInvalidPathParametersBeforeAnyRequest(string? id)
    {
        var (client, handler) = Fake.Client();
        var error = await Assert.ThrowsAsync<LettermintConfigException>(() => client.Domains.RetrieveAsync(id!));
        Assert.Equal("Domains.RetrieveAsync: domainId must be a non-empty string other than \".\" and \"..\".", error.Message);
        var nested = await Assert.ThrowsAsync<LettermintConfigException>(() => client.Webhooks.Deliveries.RetrieveAsync("w", id!));
        Assert.StartsWith("Webhooks.Deliveries.RetrieveAsync: deliveryId", nested.Message);
        Assert.Empty(handler.Requests);
    }
}
