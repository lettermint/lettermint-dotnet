using Lettermint.Internal;
using Lettermint.Models;

namespace Lettermint;

/// <summary>Routes of a project. Needs the team token.</summary>
public sealed class Routes : ApiResource
{
    internal Routes(Transport transport)
        : base(transport)
    {
    }

    /// <summary>Lists the routes of a project, one page at a time.</summary>
    public Task<ListRoutesResponse> ListAsync(string projectId, ListRoutesQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.ListRoutes, "Routes.ListAsync", [projectId], query, default, options, cancellationToken);

    /// <summary>Iterates over every route of a project, following <c>next_cursor</c>.</summary>
    public IAsyncEnumerable<RouteListData> IterateAsync(string projectId, ListRoutesQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.PaginateAsync<ListRoutesQuery, ListRoutesResponse, RouteListData>(Operations.ListRoutes, "Routes.IterateAsync", [projectId], query, options, cancellationToken);

    /// <summary>Creates a route in a project.</summary>
    public Task<RouteMutationResponse> CreateAsync(string projectId, StoreRouteData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.CreateRoute, "Routes.CreateAsync", [projectId], default, body, options, cancellationToken);

    /// <summary>Retrieves a route.</summary>
    public Task<RouteData> RetrieveAsync(string routeId, GetRouteQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetRoute, "Routes.RetrieveAsync", [routeId], query, default, options, cancellationToken);

    /// <summary>Updates a route.</summary>
    public Task<RouteMutationResponse> UpdateAsync(string routeId, UpdateRouteData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.UpdateRoute, "Routes.UpdateAsync", [routeId], default, body, options, cancellationToken);

    /// <summary>Deletes a route.</summary>
    public Task<MessageResponse> DeleteAsync(string routeId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.DeleteRoute, "Routes.DeleteAsync", [routeId], default, default, options, cancellationToken);

    /// <summary>Checks the DNS of the route's inbound domain.</summary>
    public Task<InboundDomainVerificationResponse> VerifyInboundDomainAsync(string routeId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.VerifyRouteInboundDomain, "Routes.VerifyInboundDomainAsync", [routeId], default, default, options, cancellationToken);
}
