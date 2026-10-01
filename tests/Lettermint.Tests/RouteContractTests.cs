using System.Text.Json;
using Lettermint.Models;
using Xunit;

namespace Lettermint.Tests;

public class RouteContractTests
{
    [Theory]
    [InlineData("incoming.example.com")]
    [InlineData(null)]
    public void InboundRouteDomainIsNullable(string? domain)
    {
        var payload = JsonSerializer.Serialize(new { inbound_route_domain = domain });
        var route = JsonSerializer.Deserialize<RouteData>(payload, LettermintJson.Options)!;
        Assert.Equal(domain, route.InboundRouteDomain);
        var decoded = JsonSerializer.Deserialize<RouteData>(
            JsonSerializer.Serialize(route, LettermintJson.Options), LettermintJson.Options)!;
        Assert.Equal(domain, decoded.InboundRouteDomain);
        Assert.Null(JsonSerializer.Deserialize<RouteData>("{}", LettermintJson.Options)!.InboundRouteDomain);
    }
}
