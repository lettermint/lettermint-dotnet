using Lettermint.Internal;
using Lettermint.Models;

namespace Lettermint;

/// <summary>Sending domains. Needs the team token.</summary>
public sealed class Domains : ApiResource
{
    internal Domains(Transport transport)
        : base(transport)
    {
    }

    /// <summary>Lists domains, one page at a time.</summary>
    public Task<ListDomainsResponse> ListAsync(ListDomainsQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.ListDomains, "Domains.ListAsync", [], query, default, options, cancellationToken);

    /// <summary>Iterates over every domain, following <c>next_cursor</c>.</summary>
    public IAsyncEnumerable<DomainListData> IterateAsync(ListDomainsQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.PaginateAsync<ListDomainsQuery, ListDomainsResponse, DomainListData>(Operations.ListDomains, "Domains.IterateAsync", [], query, options, cancellationToken);

    /// <summary>Adds a domain.</summary>
    public Task<DomainData> CreateAsync(StoreDomainData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.CreateDomain, "Domains.CreateAsync", [], default, body, options, cancellationToken);

    /// <summary>Retrieves a domain. <c>Include</c> adds DNS records or projects.</summary>
    public Task<DomainData> RetrieveAsync(string domainId, GetDomainQuery? query = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.GetDomain, "Domains.RetrieveAsync", [domainId], query, default, options, cancellationToken);

    /// <summary>Deletes a domain.</summary>
    public Task<MessageResponse> DeleteAsync(string domainId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.DeleteDomain, "Domains.DeleteAsync", [domainId], default, default, options, cancellationToken);

    /// <summary>Checks every DNS record of the domain.</summary>
    public Task<DnsVerificationSuccessResponse> VerifyDnsRecordsAsync(string domainId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.VerifyDomainDnsRecords, "Domains.VerifyDnsRecordsAsync", [domainId], default, default, options, cancellationToken);

    /// <summary>Checks one DNS record of the domain.</summary>
    public Task<MessageResponse> VerifyDnsRecordAsync(string domainId, string recordId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.VerifyDomainDnsRecord, "Domains.VerifyDnsRecordAsync", [domainId, recordId], default, default, options, cancellationToken);

    /// <summary>Replaces the projects that may send from the domain. An empty list makes it available to every project in the team.</summary>
    public Task<DomainMutationResponse> UpdateProjectsAsync(string domainId, UpdateDomainProjectsData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.UpdateDomainProjects, "Domains.UpdateProjectsAsync", [domainId], default, body, options, cancellationToken);

    /// <summary>
    /// Starts moving a domain with a legacy DKIM TXT record (<c>legacy_txt</c>) to managed DKIM. The response lists two
    /// new DKIM CNAME records; add them to your DNS. The current TXT record keeps signing until the upgrade completes.
    /// When <c>Data.DkimTransition.State</c> is <c>ready_to_activate</c>, call <see cref="ActivateDkimUpgradeAsync"/>.
    /// Calling this again returns the upgrade in progress; a domain that already uses managed DKIM is not changed.
    /// The API answers 422 (<see cref="ValidationException"/>, <c>DKIM_UPGRADE_UNAVAILABLE</c>) when the domain
    /// cannot be upgraded.
    /// </summary>
    public Task<DomainMutationResponse> StartDkimUpgradeAsync(string domainId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.StartDomainDkimUpgrade, "Domains.StartDkimUpgradeAsync", [domainId], default, default, options, cancellationToken);

    /// <summary>
    /// Cancels the managed DKIM upgrade and removes its DKIM CNAME records. The domain keeps its DKIM TXT record.
    /// Without an upgrade in progress nothing changes, and a key rotation is never cancelled. An upgrade in its grace
    /// period cannot be cancelled: the API answers 422 (<see cref="ValidationException"/>,
    /// <c>DKIM_UPGRADE_IN_GRACE_PERIOD</c>).
    /// </summary>
    public Task<DomainMutationResponse> CancelDkimUpgradeAsync(string domainId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.CancelDomainDkimUpgrade, "Domains.CancelDkimUpgradeAsync", [domainId], default, default, options, cancellationToken);

    /// <summary>
    /// Activates the managed DKIM upgrade. Lettermint checks both DKIM CNAME records again, then starts a grace period
    /// in which both keys are published. The upgrade completes at <c>Data.DkimTransition.CompletesAfter</c>; after that
    /// you can remove the old DKIM TXT record. The API answers 422 (<see cref="ValidationException"/>,
    /// <c>NO_DKIM_UPGRADE</c> or <c>DKIM_UPGRADE_NOT_READY</c>) when there is no upgrade or it is not ready.
    /// </summary>
    public Task<DomainMutationResponse> ActivateDkimUpgradeAsync(string domainId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.ActivateDomainDkimUpgrade, "Domains.ActivateDkimUpgradeAsync", [domainId], default, default, options, cancellationToken);

    /// <summary>
    /// Starts replacing the managed DKIM key of a domain early (Lettermint also rotates keys on a schedule). The DKIM
    /// CNAME records stay the same, so no DNS change is needed. Calling this again returns the rotation in progress.
    /// The API answers 422 (<see cref="ValidationException"/>, <c>DKIM_ROTATION_UNAVAILABLE</c>) when the domain
    /// cannot rotate its key.
    /// </summary>
    public Task<DomainMutationResponse> RotateDkimKeyAsync(string domainId, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.RotateDomainDkimKey, "Domains.RotateDkimKeyAsync", [domainId], default, default, options, cancellationToken);
}
