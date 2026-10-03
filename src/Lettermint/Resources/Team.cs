using Lettermint.Internal;
using Lettermint.Models;

namespace Lettermint;

/// <summary>Sending statistics. Needs the team token.</summary>
public sealed class Stats : ApiResource
{
    internal Stats(Transport transport)
        : base(transport)
    {
    }

    /// <summary>Daily statistics between <c>From</c> and <c>To</c> (Y-m-d, at most 90 days).</summary>
    public Task<StatsData> RetrieveAsync(GetStatsQuery query, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetStats, "Stats.RetrieveAsync", [], query, default, options, cancellationToken);
}

/// <summary>The suppression list. Needs the team token.</summary>
public sealed class Suppressions : ApiResource
{
    internal Suppressions(Transport transport)
        : base(transport)
    {
    }

    /// <summary>Lists suppressions, one page at a time.</summary>
    public Task<ListSuppressionsResponse> ListAsync(ListSuppressionsQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.ListSuppressions, "Suppressions.ListAsync", [], query, default, options, cancellationToken);

    /// <summary>Iterates over every suppression, following <c>next_cursor</c>.</summary>
    public IAsyncEnumerable<SuppressedRecipientData> IterateAsync(ListSuppressionsQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.PaginateAsync<ListSuppressionsQuery, ListSuppressionsResponse, SuppressedRecipientData>(Operations.ListSuppressions, "Suppressions.IterateAsync", [], query, options, cancellationToken);

    /// <summary>Adds addresses to the suppression list.</summary>
    public Task<SuppressionStoreResponse> CreateAsync(StoreSuppressionData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.CreateSuppressions, "Suppressions.CreateAsync", [], default, body, options, cancellationToken);

    /// <summary>Removes a suppression. HTTP 202 with a review ticket means it was not removed yet; check <c>Status</c>.</summary>
    public Task<DeleteSuppressionResponse> DeleteAsync(string suppressionId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.DeleteSuppression, "Suppressions.DeleteAsync", [suppressionId], default, default, options, cancellationToken);
}

/// <summary>Team members. Needs the team token.</summary>
public sealed class TeamMembers : ApiResource
{
    internal TeamMembers(Transport transport)
        : base(transport)
    {
    }

    /// <summary>Lists team members, one page at a time.</summary>
    public Task<ListTeamMembersResponse> ListAsync(ListTeamMembersQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.ListTeamMembers, "Team.Members.ListAsync", [], query, default, options, cancellationToken);

    /// <summary>Iterates over every team member, following <c>next_cursor</c>.</summary>
    public IAsyncEnumerable<TeamMemberData> IterateAsync(ListTeamMembersQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.PaginateAsync<ListTeamMembersQuery, ListTeamMembersResponse, TeamMemberData>(Operations.ListTeamMembers, "Team.Members.IterateAsync", [], query, options, cancellationToken);

    /// <summary>Retrieves a team member.</summary>
    public Task<TeamMemberData> RetrieveAsync(string userId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetTeamMember, "Team.Members.RetrieveAsync", [userId], default, default, options, cancellationToken);

    /// <summary>Changes a member's role and project access.</summary>
    public Task<TeamMemberData> UpdateAssignmentAsync(string userId, UpdateTeamMemberAssignmentData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.UpdateTeamMemberAssignment, "Team.Members.UpdateAssignmentAsync", [userId], default, body, options, cancellationToken);
}

/// <summary>The team of the token. Needs the team token.</summary>
public sealed class Team : ApiResource
{
    internal Team(Transport transport)
        : base(transport)
    {
        Members = new TeamMembers(transport);
    }

    /// <summary>Team members.</summary>
    public TeamMembers Members { get; }

    /// <summary>Retrieves the team. <c>Include</c> adds features or add-ons.</summary>
    public Task<TeamData> RetrieveAsync(GetTeamQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetTeam, "Team.RetrieveAsync", [], query, default, options, cancellationToken);

    /// <summary>Updates the team.</summary>
    public Task<TeamMutationResponse> UpdateAsync(UpdateTeamData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.UpdateTeam, "Team.UpdateAsync", [], default, body, options, cancellationToken);

    /// <summary>Usage of the current and previous billing periods.</summary>
    public Task<TeamUsageDetailData> UsageAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetTeamUsage, "Team.UsageAsync", [], default, default, options, cancellationToken);

    /// <summary>The roles that can be assigned to members.</summary>
    public Task<TeamRoleListResponse> RolesAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.ListTeamRoles, "Team.RolesAsync", [], default, default, options, cancellationToken);
}
