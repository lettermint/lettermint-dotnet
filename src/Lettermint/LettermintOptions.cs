using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Lettermint;

/// <summary>Options of <see cref="LettermintClient"/>. Pass at least one token.</summary>
[DebuggerDisplay("{ToString(),nq}")]
public sealed class LettermintOptions
{
    /// <summary>The default API base URL, <c>https://api.lettermint.co/v1</c>.</summary>
    public static readonly Uri DefaultBaseUrl = new("https://api.lettermint.co/v1");

    /// <summary>The default request timeout, 30 seconds.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A project sending token (<c>lm_…</c>), sent as <c>x-lettermint-token</c>.
    /// Used by <see cref="LettermintClient.Emails"/>.
    /// </summary>
    [JsonIgnore]
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public string? SendingToken { get; init; }

    /// <summary>
    /// A team API token (<c>lm_team_…</c>), sent as <c>Authorization: Bearer</c>.
    /// Used by every other part of the client.
    /// </summary>
    [JsonIgnore]
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public string? TeamToken { get; init; }

    /// <summary>The API base URL. Default <c>https://api.lettermint.co/v1</c>.</summary>
    public Uri BaseUrl { get; init; } = DefaultBaseUrl;

    /// <summary>The request timeout. It covers the response headers and the body. Default 30 seconds.</summary>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>
    /// An <see cref="System.Net.Http.HttpClient"/> to send requests with, for example one from
    /// <c>IHttpClientFactory</c>. The caller keeps ownership; the client does not dispose it.
    /// Its handler must not follow redirects (<c>AllowAutoRedirect = false</c>) and it must
    /// not have default authentication headers. Default: a client owned by the SDK whose
    /// handler never follows redirects.
    /// </summary>
    [JsonIgnore]
    public HttpClient? HttpClient { get; init; }

    /// <summary>The options without credentials: tokens are shown as <c>[redacted]</c>.</summary>
    public override string ToString() =>
        $"LettermintOptions {{ BaseUrl = {BaseUrl}, Timeout = {Timeout}, SendingToken = {Redaction.Show(SendingToken)}, TeamToken = {Redaction.Show(TeamToken)}, HttpClient = {(HttpClient is null ? "(default)" : "(custom)")} }}";
}

/// <summary>Per-call options accepted by every SDK method that makes a request.</summary>
public class RequestOptions
{
    /// <summary>Overrides the client's timeout for this call.</summary>
    public TimeSpan? Timeout { get; init; }
}

/// <summary>Options of calls that accept an <c>Idempotency-Key</c>.</summary>
public sealed class IdempotentRequestOptions : RequestOptions
{
    /// <summary>
    /// Sent as the <c>Idempotency-Key</c> header. Retrying with the same key does
    /// not send the email again. Never stored on the client.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}

internal static class Redaction
{
    public const string Redacted = "[redacted]";

    public static string Show(string? secret) => secret is null ? "(not set)" : Redacted;
}
