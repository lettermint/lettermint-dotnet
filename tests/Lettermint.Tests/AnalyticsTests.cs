using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lettermint.Internal;
using Lettermint.Models;

namespace Lettermint.Tests;

public class AnalyticsTests
{
    private static readonly AnalyticsQuery Query = new()
    {
        Metrics = [AnalyticsMetric.Delivered, AnalyticsMetric.Bounced, AnalyticsMetric.DeliveryRate, AnalyticsMetric.DeliveryLatencyP50Ms],
        Include = [AnalyticsSection.Summary, AnalyticsSection.TimeSeries, AnalyticsSection.Breakdown],
        GroupBy = [AnalyticsCatalogueGroupDimension.RecipientDomain],
        Interval = AnalyticsInterval.Hour,
        Timezone = "Asia/Kolkata",
        Compare = AnalyticsComparison.PreviousPeriod,
        Limit = 2,
    };

    private const string QueryJson = """
        {"metrics":["delivered","bounced","delivery_rate","delivery_latency_p50_ms"],"include":["summary","time_series","breakdown"],"group_by":["recipient_domain"],"interval":"hour","timezone":"Asia/Kolkata","compare":"previous_period","limit":2}
        """;

    private static readonly string PageOne = Fixture("analytics-page-1");
    private static readonly string PageTwo = Fixture("analytics-page-2");
    private static readonly string SummaryOnly = Fixture("analytics-summary");

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json"));

    private static HttpResponseMessage Page(string json) => Fake.Text(200, json, "application/json");

    /// <summary>The query as JSON, with the cursor of a later page.</summary>
    private static JsonNode Body(string? cursor = null)
    {
        var body = JsonNode.Parse(QueryJson)!;
        if (cursor is not null)
        {
            body["cursor"] = cursor;
        }
        return body;
    }

    private static void AssertBody(JsonNode expected, RecordedRequest request) =>
        Assert.True(JsonNode.DeepEquals(expected, request.Json), request.Body);

    private static DateTimeOffset Instant(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    [Fact]
    public async Task KeepsNullValuesEmptyRateBasesAndBothTimestampFormats()
    {
        var (client, handler) = Fake.Client((_, _) => Page(PageOne));
        var result = await client.AnalyticsAsync(Query);

        AssertBody(Body(), handler.Requests[0]);
        var summary = result.Data.Summary!;
        Assert.Equal(new AnalyticsMetricValues { Delivered = 1200, Bounced = 0, DeliveryRate = 0.9836, DeliveryLatencyP50Ms = null }, summary.Metrics);
        Assert.Equal(0, summary.Metrics.Bounced);
        Assert.Null(summary.Metrics.DeliveryLatencyP50Ms);
        Assert.Equal(new AnalyticsRateBase { Numerator = 1200, Denominator = 1220 }, summary.RateBases.DeliveryRate);
        Assert.Equal(new AnalyticsRateBase { Numerator = null, Denominator = null }, summary.Previous!.RateBases.DeliveryRate);
        Assert.Null(summary.Previous.Metrics.DeliveryRate);
        Assert.Equal(812.5, summary.Previous.Metrics.DeliveryLatencyP50Ms);
        Assert.Equal(new AnalyticsMetricChange { Absolute = null, Relative = null, PercentagePoints = null }, summary.Change!["delivery_rate"]);
        Assert.Equal(new AnalyticsMetricChange { Absolute = 100, Relative = 0.0909 }, summary.Change["delivered"]);
        Assert.Null(summary.Change["delivered"].PercentagePoints);

        var series = result.Data.TimeSeries!;
        Assert.Equal(2, series.Count);
        Assert.Equal("2026-09-15T08:30:00+05:30", series[0].From);
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 3, 0, 0, TimeSpan.Zero), Instant(series[0].From));
        Assert.False(series[1].Available);
        Assert.True(series[1].Partial);
        Assert.Equal(new AnalyticsRateBases(), series[1].RateBases);
        Assert.Null(series[1].Metrics.Delivered);

        Assert.Equal("2026-09-15T04:12:30.482915Z", result.Meta.GeneratedAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 4, 12, 30, TimeSpan.Zero).AddTicks(4829150), Instant(result.Meta.GeneratedAt));
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 3, 0, 0, TimeSpan.Zero), Instant(result.Meta.From));
        Assert.Null(result.Meta.LastIngestedAt);
        Assert.Equal(new AnalyticsMetaComparison { From = "2026-09-15T01:00:00.000000Z", To = "2026-09-15T03:00:00.000000Z", Partial = false }, result.Meta.Comparison);
        Assert.Equal(new AnalyticsPagination { TotalGroups = 3, ReturnedGroups = 2, NextCursor = "cursor-page-2", Truncated = false }, result.Pagination);
    }

    [Fact]
    public async Task LeavesOutTheSectionsAndComparisonAQueryDidNotAskFor()
    {
        var (client, _) = Fake.Client((_, _) => Page(SummaryOnly));
        var result = await client.AnalyticsAsync(new AnalyticsQuery { Metrics = [AnalyticsMetric.Delivered] });
        Assert.Equal(new AnalyticsRateBases(), result.Data.Summary!.RateBases);
        Assert.Null(result.Data.TimeSeries);
        Assert.Null(result.Data.Breakdown);
        Assert.Null(result.Data.Summary.Previous);
        Assert.Null(result.Data.Summary.Change);
        Assert.Null(result.Meta.Comparison);
        Assert.Null(result.Pagination.NextCursor);
    }

    [Fact]
    public async Task KeepsANullDimensionValueInABreakdownRow()
    {
        var (client, _) = Fake.Client((_, _) => Page(PageTwo));
        var result = await client.AnalyticsAsync(Query with { Cursor = "cursor-page-2" });
        var row = Assert.Single(result.Data.Breakdown!);
        var dimension = Assert.Single(row.Dimensions);
        Assert.Equal("recipient_domain", dimension.Key);
        Assert.Null(dimension.Value);
        Assert.Equal(50, row.Metrics.Delivered);
        Assert.Null(row.Metrics.Bounced);
    }

    [Fact]
    public async Task PagesFollowNextCursorAndYieldEveryResponse()
    {
        var sent = Query with { };
        var (client, handler) = Fake.Client((_, index) => Page(index == 0 ? PageOne : PageTwo));
        var pages = new List<AnalyticsResponse>();
        await foreach (var page in client.AnalyticsPagesAsync(sent))
        {
            pages.Add(page);
        }

        Assert.Equal(2, pages.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("POST", request.Method));
        Assert.All(handler.Requests, request => Assert.Equal("https://api.lettermint.co/v1/analytics", request.Url.AbsoluteUri));
        Assert.All(handler.Requests, request => Assert.Equal("Bearer " + Fake.TeamToken, request.Header("Authorization")));
        AssertBody(Body(), handler.Requests[0]);
        AssertBody(Body("cursor-page-2"), handler.Requests[1]);
        Assert.Null(sent.Cursor);
        Assert.True(JsonNode.DeepEquals(Body(), JsonSerializer.SerializeToNode(sent, LettermintJson.Options)));

        var rows = pages.SelectMany(page => page.Data.Breakdown ?? []);
        Assert.Equal(["gmail.com", "outlook.com", null], rows.Select(row => row.Dimensions["recipient_domain"]));
        Assert.Equal(1, pages[1].Pagination.ReturnedGroups);
        Assert.Null(pages[1].Pagination.NextCursor);
    }

    [Fact]
    public async Task PagesMakeOneRequestForAQueryWithoutMorePages()
    {
        var (client, handler) = Fake.Client((_, _) => Page(SummaryOnly));
        var pages = new List<AnalyticsResponse>();
        await foreach (var page in client.AnalyticsPagesAsync(new AnalyticsQuery { Metrics = [AnalyticsMetric.Delivered] }))
        {
            pages.Add(page);
        }
        Assert.Single(pages);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PagesRequestTheNextPageOnlyWhenItIsAskedFor()
    {
        var (client, handler) = Fake.Client((_, _) => Page(PageOne));
        Assert.Empty(handler.Requests);
        await foreach (var page in client.AnalyticsPagesAsync(Query))
        {
            Assert.Equal("cursor-page-2", page.Pagination.NextCursor);
            break;
        }
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PagesStopWhenTheApiRepeatsACursor()
    {
        var (client, handler) = Fake.Client((_, _) => Page(PageOne));
        var count = 0;
        await foreach (var _ in client.AnalyticsPagesAsync(Query))
        {
            count++;
        }
        Assert.Equal(2, count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task PagesStopWhenTheApiReturnsTheCursorTheQueryStartedFrom()
    {
        var (client, handler) = Fake.Client((_, _) => Page(PageOne));
        var count = 0;
        await foreach (var _ in client.AnalyticsPagesAsync(Query with { Cursor = "cursor-page-2" }))
        {
            count++;
        }
        Assert.Equal(1, count);
        AssertBody(Body("cursor-page-2"), Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task PagesPassRequestOptionsAndTheCancellationTokenToEveryRequest()
    {
        var (client, handler) = Fake.Client(async (_, index, cancellationToken) =>
        {
            if (index > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return Page(PageOne);
        });
        await using (var pages = client.AnalyticsPagesAsync(Query, new RequestOptions { Timeout = TimeSpan.FromMilliseconds(250) }).GetAsyncEnumerator())
        {
            Assert.True(await pages.MoveNextAsync());
            var timeout = await Assert.ThrowsAsync<LettermintTimeoutException>(async () => await pages.MoveNextAsync());
            Assert.Equal(TimeSpan.FromMilliseconds(250), timeout.Timeout);
            Assert.Equal(2, handler.Requests.Count);
        }

        using var source = new CancellationTokenSource();
        var (other, otherHandler) = Fake.Client(async (_, index, cancellationToken) =>
        {
            if (index > 0)
            {
                source.Cancel();
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return Page(PageOne);
        });
        await using (var pages = other.AnalyticsPagesAsync(Query, cancellationToken: source.Token).GetAsyncEnumerator())
        {
            Assert.True(await pages.MoveNextAsync());
            var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pages.MoveNextAsync());
            Assert.Equal(source.Token, canceled.CancellationToken);
            Assert.Equal(2, otherHandler.Requests.Count);
        }

        // await foreach (… in AnalyticsPagesAsync(query).WithCancellation(token)) passes the token this way.
        using var enumeration = new CancellationTokenSource();
        var (third, thirdHandler) = Fake.Client((_, _) => Page(PageOne));
        await using (var pages = third.AnalyticsPagesAsync(Query).GetAsyncEnumerator(enumeration.Token))
        {
            Assert.True(await pages.MoveNextAsync());
            enumeration.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pages.MoveNextAsync());
            Assert.Single(thirdHandler.Requests);
        }
    }

    [Fact]
    public async Task PagesSurfaceAnExpiredCursorAsAValidationException()
    {
        const string message = "The analytics cursor is invalid or expired. Submit a new query.";
        var (client, handler) = Fake.Client((_, index) => index == 0 ? Page(PageOne) : Fake.Json(422, new { message, errors = new { cursor = new[] { message } } }));
        await using var pages = client.AnalyticsPagesAsync(Query).GetAsyncEnumerator();
        Assert.True(await pages.MoveNextAsync());
        var error = await Assert.ThrowsAsync<ValidationException>(async () => await pages.MoveNextAsync());
        Assert.Equal(message, error.Message);
        Assert.Equal([message], error.Errors!["cursor"]);
        Assert.Single(error.Errors);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task PagesNeedTheTeamToken()
    {
        var (client, handler) = Fake.Client(team: false);
        var error = await Assert.ThrowsAsync<LettermintConfigException>(async () =>
        {
            await foreach (var _ in client.AnalyticsPagesAsync(Query))
            {
            }
        });
        Assert.Equal("AnalyticsPagesAsync needs TeamToken; set LettermintOptions.TeamToken.", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ReadsFieldErrorsFromA422()
    {
        const string message = "smtp_response_group can only be used in group_by.";
        var (client, _) = Fake.Client((_, _) => Fake.Json(422, new { message, errors = new { filters = new[] { message } } }));
        var error = await Assert.ThrowsAsync<ValidationException>(() => client.AnalyticsAsync(Query));
        Assert.Equal(422, (int)error.StatusCode);
        Assert.Equal(message, error.Message);
        Assert.Equal([message], error.Errors!["filters"]);
        Assert.Single(error.Errors);
    }

    [Fact]
    public async Task ReadsRetryAfterFromA503()
    {
        var (client, _) = Fake.Client((_, _) =>
        {
            var response = Fake.Json(503, new { error = new { code = "SERVICE_UNAVAILABLE", message = "Try again shortly." } });
            response.Headers.Add("Retry-After", "2");
            return response;
        });
        var error = await Assert.ThrowsAsync<ServerException>(() => client.AnalyticsAsync(Query));
        Assert.Equal(503, (int)error.StatusCode);
        Assert.Equal("SERVICE_UNAVAILABLE", error.Code);
        Assert.Equal("Try again shortly.", error.Message);
        Assert.Equal(TimeSpan.FromSeconds(2), error.RetryAfter);
    }

    [Fact]
    public async Task HasNoRetryAfterForA503Or504WithoutTheHeader()
    {
        var (unavailable, _) = Fake.Client((_, _) => Fake.Json(503, new { message = "Analytics is unavailable." }));
        var first = await Assert.ThrowsAsync<ServerException>(() => unavailable.AnalyticsAsync(Query));
        Assert.Equal(503, (int)first.StatusCode);
        Assert.Null(first.RetryAfter);

        const string message = "Analytics exceeded the query time limit. Retry with a shorter period or fewer dimensions.";
        var (timedOut, _) = Fake.Client((_, _) => Fake.Json(504, new { message }));
        var second = await Assert.ThrowsAsync<ServerException>(() => timedOut.AnalyticsAsync(Query));
        Assert.Equal(504, (int)second.StatusCode);
        Assert.Equal(message, second.Message);
        Assert.Null(second.RetryAfter);
    }
}
