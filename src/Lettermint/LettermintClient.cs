using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lettermint.Internal;
using Lettermint.Models;

namespace Lettermint;

/// <summary>
/// The Lettermint client.
/// </summary>
/// <remarks>
/// <see cref="Emails"/> uses the sending token; every other part uses the team
/// token. The client holds no message state and is safe to share across threads
/// and requests: create it once (for example as a singleton) and reuse it.
/// <code>
/// var lettermint = new LettermintClient(new LettermintOptions { SendingToken = sending, TeamToken = team });
/// var lettermint = new LettermintClient("lm_..."); // team or sending token, detected by prefix
/// </code>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public sealed class LettermintClient : IDisposable
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly Transport _transport;

    /// <summary>Creates a client from options. Pass at least one token.</summary>
    /// <exception cref="LettermintConfigException">No token, an invalid token or an invalid option.</exception>
    public LettermintClient(LettermintOptions options)
    {
        if (options is null)
        {
            throw new LettermintConfigException("Pass LettermintOptions with SendingToken, TeamToken or both.");
        }
        var sendingToken = Tokens.Check("SendingToken", options.SendingToken);
        var teamToken = Tokens.Check("TeamToken", options.TeamToken);
        if (sendingToken is null && teamToken is null)
        {
            throw new LettermintConfigException("Pass SendingToken, TeamToken or both in LettermintOptions.");
        }
        _transport = new Transport(
            sendingToken,
            teamToken,
            Transport.CheckBaseUrl(options.BaseUrl),
            Transport.CheckTimeout(options.Timeout),
            options.HttpClient);
        Emails = new Emails(_transport);
        Domains = new Domains(_transport);
        Messages = new Messages(_transport);
        Projects = new Projects(_transport);
        Routes = new Routes(_transport);
        Stats = new Stats(_transport);
        Suppressions = new Suppressions(_transport);
        Team = new Team(_transport);
        Webhooks = new Webhooks(_transport);
    }

    /// <summary>
    /// Creates a client from one token. <c>lm_team_…</c> followed by letters and digits
    /// is a team token; any other <c>lm_…</c> token of letters and digits is a sending token.
    /// </summary>
    /// <param name="token">The token. Its type is detected by its format.</param>
    /// <param name="options">Other options. Leave <see cref="LettermintOptions.SendingToken"/> and <see cref="LettermintOptions.TeamToken"/> unset.</param>
    /// <exception cref="LettermintConfigException">The token format is not recognised (for example an SSO token), or the options also hold a token.</exception>
    public LettermintClient(string token, LettermintOptions? options = null)
        : this(WithToken(token, options))
    {
    }

    private static LettermintOptions WithToken(string token, LettermintOptions? options)
    {
        var kind = Tokens.Detect(token);
        options ??= new LettermintOptions();
        if (options.SendingToken is not null || options.TeamToken is not null)
        {
            throw new LettermintConfigException("Pass the token either as the first argument or in LettermintOptions, not both.");
        }
        return new LettermintOptions
        {
            SendingToken = kind == TokenKind.Sending ? token : null,
            TeamToken = kind == TokenKind.Team ? token : null,
            BaseUrl = options.BaseUrl,
            Timeout = options.Timeout,
            HttpClient = options.HttpClient,
        };
    }

    /// <summary>Send email. Needs the sending token.</summary>
    public Emails Emails { get; }

    /// <summary>Sending domains. Needs the team token.</summary>
    public Domains Domains { get; }

    /// <summary>Sent and received messages. Needs the team token.</summary>
    public Messages Messages { get; }

    /// <summary>Projects and their report forwarding. Needs the team token.</summary>
    public Projects Projects { get; }

    /// <summary>Routes of a project. Needs the team token.</summary>
    public Routes Routes { get; }

    /// <summary>Sending statistics. Needs the team token.</summary>
    public Stats Stats { get; }

    /// <summary>The suppression list. Needs the team token.</summary>
    public Suppressions Suppressions { get; }

    /// <summary>The team and its members. Needs the team token.</summary>
    public Team Team { get; }

    /// <summary>Webhook endpoints and their deliveries. Needs the team token.</summary>
    public Webhooks Webhooks { get; }

    /// <summary>The API base URL, without a trailing slash.</summary>
    public string BaseUrl => _transport.BaseUrl;

    /// <summary>The default request timeout.</summary>
    public TimeSpan Timeout => _transport.Timeout;

    /// <summary>
    /// Checks the configured token: <c>GET /ping</c> returns <c>pong</c>. Uses the team
    /// token when configured, otherwise the sending token.
    /// </summary>
    public async Task<string> PingAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var text = await _transport.CallAsync(Operations.Ping, "PingAsync", [], default, default, options, cancellationToken).ConfigureAwait(false);
        return text.Trim();
    }

    /// <summary>Queries email analytics. Needs the team token.</summary>
    public Task<AnalyticsResponse> AnalyticsAsync(AnalyticsQuery query, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => _transport.CallAsync(Operations.QueryAnalytics, "AnalyticsAsync", [], default, query, options, cancellationToken);

    /// <summary>
    /// Queries email analytics and follows <c>pagination.next_cursor</c>, yielding one
    /// whole response per request. Each response carries the next page of
    /// <c>Data.Breakdown</c> with its own <c>Meta</c> and <c>Pagination</c>. Needs the team token.
    /// </summary>
    /// <remarks>
    /// A cursor expires 60 seconds after its response, so request the next page
    /// promptly; an expired cursor throws a <see cref="ValidationException"/>. The
    /// query passed in is not changed.
    /// </remarks>
    public async IAsyncEnumerable<AnalyticsResponse> AnalyticsPagesAsync(AnalyticsQuery query, RequestOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (query.Cursor is { } start)
        {
            seen.Add(start);
        }
        var body = query;
        while (true)
        {
            var page = await _transport.CallAsync(Operations.QueryAnalytics, "AnalyticsPagesAsync", [], default, body, options, cancellationToken).ConfigureAwait(false);
            yield return page;
            var next = page.Pagination?.NextCursor;
            if (string.IsNullOrEmpty(next) || !seen.Add(next))
            {
                yield break;
            }
            body = query with { Cursor = next };
        }
    }

    /// <summary>The file extensions and MIME types that cannot be attached. Needs the team token.</summary>
    public Task<BlockedFileTypes> BlockedFileTypesAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
        => _transport.CallAsync(Operations.ListBlockedFileTypes, "BlockedFileTypesAsync", [], default, default, options, cancellationToken);

    /// <summary>The configuration without credentials: tokens are shown as <c>[redacted]</c>.</summary>
    public override string ToString() =>
        $"LettermintClient {{ BaseUrl = {_transport.BaseUrl}, Timeout = {_transport.Timeout}, SendingToken = {(_transport.HasSendingToken ? Redaction.Redacted : "(not set)")}, TeamToken = {(_transport.HasTeamToken ? Redaction.Redacted : "(not set)")} }}";

    /// <summary>Disposes the HttpClient the SDK created. A caller-supplied HttpClient is left alone.</summary>
    public void Dispose() => _transport.Dispose();
}
