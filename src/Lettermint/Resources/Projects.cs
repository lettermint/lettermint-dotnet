using Lettermint.Internal;
using Lettermint.Models;

namespace Lettermint;

/// <summary>DMARC and complaint report forwarding of a project. Needs the team token.</summary>
public sealed class ReportForwarding : ApiResource
{
    internal ReportForwarding(Transport transport)
        : base(transport)
    {
    }

    /// <summary>Retrieves the report forwarding settings.</summary>
    public Task<GetReportForwardingResponse> RetrieveAsync(string projectId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetReportForwarding, "Projects.ReportForwarding.RetrieveAsync", [projectId], default, default, options, cancellationToken);

    /// <summary>Sets the forwarding address.</summary>
    public Task<UpdateReportForwardingResponse> UpdateAsync(string projectId, ReportForwardingRequest body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.UpdateReportForwarding, "Projects.ReportForwarding.UpdateAsync", [projectId], default, body, options, cancellationToken);

    /// <summary>Disables report forwarding (HTTP 204).</summary>
    public Task DeleteAsync(string projectId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.DeleteReportForwarding, "Projects.ReportForwarding.DeleteAsync", [projectId], default, default, options, cancellationToken);

    /// <summary>Verifies the forwarding address with the emailed code.</summary>
    public Task<VerifyReportForwardingResponse> VerifyAsync(string projectId, VerifyReportForwardingRequest body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.VerifyReportForwarding, "Projects.ReportForwarding.VerifyAsync", [projectId], default, body, options, cancellationToken);

    /// <summary>Sends the verification code again.</summary>
    public Task<ResendReportForwardingCodeResponse> ResendCodeAsync(string projectId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.ResendReportForwardingCode, "Projects.ReportForwarding.ResendCodeAsync", [projectId], default, default, options, cancellationToken);
}

/// <summary>The custom tracking domain of a project, used by open and click tracking links. Needs the team token.</summary>
public sealed class TrackingDomain : ApiResource
{
    internal TrackingDomain(Transport transport)
        : base(transport)
    {
    }

    /// <summary>
    /// Retrieves the custom tracking domain and whether its CNAME record is verified. <c>Domain</c> is null when the
    /// project uses the Lettermint tracking domain.
    /// </summary>
    public Task<ProjectTrackingDomain> RetrieveAsync(string projectId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetProjectTrackingDomain, "Projects.TrackingDomain.RetrieveAsync", [projectId], default, default, options, cancellationToken);

    /// <summary>
    /// Sets or replaces the custom tracking domain. Add a CNAME record for the domain that points to
    /// <c>Data.CnameTarget</c>; tracking links keep using the Lettermint tracking domain until it is verified. Needs a
    /// plan with custom tracking domains. A domain that another team uses is rejected.
    /// </summary>
    public Task<ProjectTrackingDomainMutationResponse> UpdateAsync(string projectId, UpdateProjectTrackingDomainData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.UpdateProjectTrackingDomain, "Projects.TrackingDomain.UpdateAsync", [projectId], default, body, options, cancellationToken);

    /// <summary>Removes the custom tracking domain (HTTP 204). Tracking links use the Lettermint tracking domain again.</summary>
    public Task DeleteAsync(string projectId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.DeleteProjectTrackingDomain, "Projects.TrackingDomain.DeleteAsync", [projectId], default, default, options, cancellationToken);

    /// <summary>
    /// Checks the CNAME record of the custom tracking domain now. When the record is missing, points elsewhere or the
    /// DNS lookup failed, the API answers 422: a <see cref="ValidationException"/> whose <c>Body</c> holds the
    /// <see cref="ProjectTrackingDomainVerificationResponse"/> (<c>TemporaryError</c> is true when the lookup failed).
    /// </summary>
    public Task<ProjectTrackingDomainVerificationResponse> VerifyAsync(string projectId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.VerifyProjectTrackingDomain, "Projects.TrackingDomain.VerifyAsync", [projectId], default, default, options, cancellationToken);
}

/// <summary>API tokens of a project. Needs the team token.</summary>
public sealed class ProjectApiTokens : ApiResource
{
    internal ProjectApiTokens(Transport transport)
        : base(transport)
    {
    }

    /// <summary>Lists the API tokens of a project, one page at a time. The token values are not included.</summary>
    public Task<ListProjectApiTokensResponse> ListAsync(string projectId, ListProjectApiTokensQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.ListProjectApiTokens, "Projects.ApiTokens.ListAsync", [projectId], query, default, options, cancellationToken);

    /// <summary>Iterates over every API token of a project, following <c>next_cursor</c>.</summary>
    public IAsyncEnumerable<ProjectApiToken> IterateAsync(string projectId, ListProjectApiTokensQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.PaginateAsync<ListProjectApiTokensQuery, ListProjectApiTokensResponse, ProjectApiToken>(Operations.ListProjectApiTokens, "Projects.ApiTokens.IterateAsync", [projectId], query, options, cancellationToken);

    /// <summary>
    /// Creates a project API token. The token (<c>Data.Token</c>) is only returned once. Use it to send through the API,
    /// or as the SMTP password when SMTP is enabled for the project; <c>Smtp</c> then holds the SMTP settings.
    /// </summary>
    public Task<ProjectApiTokenCreatedResponse> CreateAsync(string projectId, StoreProjectApiTokenData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.CreateProjectApiToken, "Projects.ApiTokens.CreateAsync", [projectId], default, body, options, cancellationToken);

    /// <summary>Deletes a project API token. The token stops working immediately.</summary>
    public Task<MessageResponse> DeleteAsync(string projectId, string tokenId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.DeleteProjectApiToken, "Projects.ApiTokens.DeleteAsync", [projectId, tokenId], default, default, options, cancellationToken);
}

/// <summary>Projects. Needs the team token.</summary>
public sealed class Projects : ApiResource
{
    internal Projects(Transport transport)
        : base(transport)
    {
        ReportForwarding = new ReportForwarding(transport);
        ApiTokens = new ProjectApiTokens(transport);
        TrackingDomain = new TrackingDomain(transport);
    }

    /// <summary>Report forwarding of a project.</summary>
    public ReportForwarding ReportForwarding { get; }

    /// <summary>API tokens of a project.</summary>
    public ProjectApiTokens ApiTokens { get; }

    /// <summary>The custom tracking domain of a project.</summary>
    public TrackingDomain TrackingDomain { get; }

    /// <summary>Lists projects, one page at a time.</summary>
    public Task<ListProjectsResponse> ListAsync(ListProjectsQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.ListProjects, "Projects.ListAsync", [], query, default, options, cancellationToken);

    /// <summary>Iterates over every project, following <c>next_cursor</c>.</summary>
    public IAsyncEnumerable<ProjectListData> IterateAsync(ListProjectsQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.PaginateAsync<ListProjectsQuery, ListProjectsResponse, ProjectListData>(Operations.ListProjects, "Projects.IterateAsync", [], query, options, cancellationToken);

    /// <summary>Creates a project. The response holds its sending token once (<c>ApiToken</c>).</summary>
    public Task<ProjectCreatedData> CreateAsync(StoreProjectData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.CreateProject, "Projects.CreateAsync", [], default, body, options, cancellationToken);

    /// <summary>Retrieves a project. <c>TrackingDomain</c> holds its custom tracking domain.</summary>
    public Task<ProjectData> RetrieveAsync(string projectId, GetProjectQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetProject, "Projects.RetrieveAsync", [projectId], query, default, options, cancellationToken);

    /// <summary>Updates a project.</summary>
    public Task<ProjectMutationResponse> UpdateAsync(string projectId, UpdateProjectData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.UpdateProject, "Projects.UpdateAsync", [projectId], default, body, options, cancellationToken);

    /// <summary>Deletes a project.</summary>
    public Task<MessageResponse> DeleteAsync(string projectId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.DeleteProject, "Projects.DeleteAsync", [projectId], default, default, options, cancellationToken);

    /// <summary>
    /// Rotates the project's legacy sending token. Use <see cref="ApiTokens"/> to list, create and delete specific tokens.
    /// </summary>
    [Obsolete("The Lettermint API marks this endpoint as legacy.")]
    public Task<RotateProjectTokenResponse> RotateTokenAsync(string projectId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.RotateProjectToken, "Projects.RotateTokenAsync", [projectId], default, default, options, cancellationToken);
}
