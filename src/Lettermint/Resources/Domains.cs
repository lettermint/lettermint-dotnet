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

    /// <summary>Replaces the projects that may send from the domain.</summary>
    public Task<DomainMutationResponse> UpdateProjectsAsync(string domainId, UpdateDomainProjectsData body, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => Transport.CallAsync(Operations.UpdateDomainProjects, "Domains.UpdateProjectsAsync", [domainId], default, body, options, cancellationToken);
}
