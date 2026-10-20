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

    /// <summary>Lists team members, one page at a time. <c>FilterProjectId</c> keeps only the members who can access that project.</summary>
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

    /// <summary>Removes a member from the team. The Owner cannot be removed.</summary>
    public Task<MessageResponse> RemoveAsync(string userId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.DeleteTeamMember, "Team.Members.RemoveAsync", [userId], default, default, options, cancellationToken);
}

/// <summary>Pending team invites. Needs the team token.</summary>
public sealed class TeamInvites : ApiResource
{
    internal TeamInvites(Transport transport)
        : base(transport)
    {
    }

    /// <summary>Lists pending invites with the role and project access they will receive, one page at a time.</summary>
    public Task<ListTeamInvitesResponse> ListAsync(ListTeamInvitesQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.ListTeamInvites, "Team.Invites.ListAsync", [], query, default, options, cancellationToken);

    /// <summary>Iterates over every pending invite, following <c>next_cursor</c>.</summary>
    public IAsyncEnumerable<TeamInviteData> IterateAsync(ListTeamInvitesQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.PaginateAsync<ListTeamInvitesQuery, ListTeamInvitesResponse, TeamInviteData>(Operations.ListTeamInvites, "Team.Invites.IterateAsync", [], query, options, cancellationToken);

    /// <summary>Retrieves a pending invite.</summary>
    public Task<TeamInviteData> RetrieveAsync(string inviteId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetTeamInvite, "Team.Invites.RetrieveAsync", [inviteId], default, default, options, cancellationToken);

    /// <summary>
    /// Invites a person to the team and sends the invitation email. The team needs a free seat on its plan;
    /// pending invites do not use one. The Owner role cannot be assigned through an invite.
    /// </summary>
    public Task<TeamInviteData> CreateAsync(CreateTeamInviteData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.CreateTeamInvite, "Team.Invites.CreateAsync", [], default, body, options, cancellationToken);

    /// <summary>Replaces the email, role and project access of a pending invite. A new email is sent only when the address changes.</summary>
    public Task<TeamInviteData> UpdateAsync(string inviteId, UpdateTeamInviteData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.UpdateTeamInvite, "Team.Invites.UpdateAsync", [inviteId], default, body, options, cancellationToken);

    /// <summary>Revokes a pending invite.</summary>
    public Task<MessageResponse> RevokeAsync(string inviteId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.DeleteTeamInvite, "Team.Invites.RevokeAsync", [inviteId], default, default, options, cancellationToken);
}

/// <summary>The team of the token. Needs the team token.</summary>
public sealed class Team : ApiResource
{
    internal Team(Transport transport)
        : base(transport)
    {
        Members = new TeamMembers(transport);
        Invites = new TeamInvites(transport);
    }

    /// <summary>Team members.</summary>
    public TeamMembers Members { get; }

    /// <summary>Pending team invites.</summary>
    public TeamInvites Invites { get; }

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

    /// <summary>Retrieves a team role. Use a role ID from <see cref="RolesAsync"/>.</summary>
    public Task<TeamRoleData> RetrieveRoleAsync(string roleId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetTeamRole, "Team.RetrieveRoleAsync", [roleId], default, default, options, cancellationToken);

    /// <summary>
    /// Creates a custom team role. Needs a plan with custom roles. Include the permissions that the chosen permissions
    /// depend on; role names are unique within the team.
    /// </summary>
    public Task<TeamRoleData> CreateRoleAsync(CreateTeamRoleData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.CreateTeamRole, "Team.CreateRoleAsync", [], default, body, options, cancellationToken);

    /// <summary>
    /// Replaces the name, description and complete permission set of a custom role. The change applies to every member,
    /// invite and SCIM group that holds the role. Built-in roles cannot be changed.
    /// </summary>
    public Task<TeamRoleData> UpdateRoleAsync(string roleId, UpdateTeamRoleData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.UpdateTeamRole, "Team.UpdateRoleAsync", [roleId], default, body, options, cancellationToken);

    /// <summary>
    /// Deletes a custom role. A role that is still assigned to members, invites or SCIM groups needs
    /// <c>ReplacementRoleId</c>, the role that receives those assignments. Built-in roles cannot be deleted.
    /// </summary>
    public Task<MessageResponse> DeleteRoleAsync(string roleId, DeleteTeamRoleQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.DeleteTeamRole, "Team.DeleteRoleAsync", [roleId], query, default, options, cancellationToken);

    /// <summary>
    /// Copies a role under a new name. Without a description, the copy keeps the source role's description.
    /// The Owner role cannot be duplicated.
    /// </summary>
    public Task<TeamRoleData> DuplicateRoleAsync(string roleId, DuplicateTeamRoleData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.DuplicateTeamRole, "Team.DuplicateRoleAsync", [roleId], default, body, options, cancellationToken);
}
