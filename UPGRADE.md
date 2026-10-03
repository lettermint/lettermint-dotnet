# Upgrade guide

# Upgrade from 1.x to 2.0

1.x no longer receives updates, including fixes. Upgrade to 2.0 to keep getting them.

2.0 is a new major version. It brings the .NET SDK in line with the other Lettermint SDKs: one client for both tokens, stateless sending with an immutable builder, typed errors for every outcome, open enums that never fail on a value the API adds later, typed query parameters with pagination helpers, and webhook verification that requires both signature headers.

## Highlights

- One client: `new LettermintClient(new LettermintOptions { SendingToken, TeamToken })`, or `new LettermintClient(token)`. It replaces `LettermintClient.Email()` and `LettermintClient.Api()`.
- Sending is stateless: `Emails.SendAsync(message, options)`, `Emails.SendBatchAsync(messages, options)` and an immutable `Emails.Compose()` builder. The idempotency key is a per-call option.
- Each part uses its own token: `Emails` uses the sending token, the Team API uses the team token. The SDK never falls back to the other token.
- Typed exceptions for every outcome, including empty or HTML responses, redirects, timeouts and network failures.
- Redirects are never followed, so tokens are never sent to another host.
- Tokens never appear in `ToString()`, the debugger or JSON output of SDK objects.
- Enums are open structs: a status the API adds later decodes instead of failing the call.
- Typed query records (`new ListDomainsQuery { PageSize = 30 }`) and `IterateAsync()` helpers that follow `next_cursor`.
- Types are generated from the current API specification and use its names (see [Type names](#type-names)).
- Webhook verification is an instance class, takes the request headers and requires `X-Lettermint-Delivery`.

## Requirements

.NET 8 or .NET 10, as before. The package has no dependencies.

## Upgrade with a coding agent

You can let a coding agent (Claude Code, Codex, Cursor, Copilot, …) do the upgrade. Copy this instruction into the agent from your solution's root, then review its changes:

````text
Upgrade this solution from the Lettermint .NET SDK 1.x (NuGet package `Lettermint`) to 2.0.

1. Update the package in every project that references it: `dotnet add <project> package Lettermint --version 2.0.0` (or edit the PackageReference / Directory.Packages.props). 2.0 targets .NET 8 and .NET 10; report any project that targets an older framework.
2. Read the upgrade guide before changing code: `UPGRADE.md` in the package folder (`~/.nuget/packages/lettermint/2.0.0/UPGRADE.md`, or `%UserProfile%\.nuget\packages\lettermint\2.0.0\UPGRADE.md` on Windows), or https://github.com/lettermint/lettermint-dotnet/blob/main/UPGRADE.md. Treat it as the source of truth and don't guess APIs; when unsure, read the XML docs in `lib/net8.0/Lettermint.xml` in the same folder.
3. Find every use of the SDK: `using Lettermint`, `LettermintClient.Email(`, `LettermintClient.Api(`, `EmailClient`, `ApiClient`, `*Endpoint`, `ClientOptions`, `RequestOptions`, `.IdempotencyKey(`, `.Attach(`, `MessageTag`, `OptionalNullable`, `Webhook.Verify(`, `LettermintApiException`, `ResponseBody`, `JsonElement` payload overloads, `switch` statements over Lettermint enums, and the 1.x type names from the guide's type-name table.
4. Rewrite each use following the guide's before/after examples:
   - Create one `LettermintClient` with `new LettermintOptions { SendingToken = ... }`, adding `TeamToken` only where the Team API is used. Register it once (for example as a singleton) instead of creating clients per request. Keep the project's existing configuration keys and environment variable names.
   - Replace builder chains with `lettermint.Emails.SendAsync(new SendMailRequest { ... })`, or with `lettermint.Emails.Compose()` per email. Builders are immutable: assign the result of every setter. Never keep a builder that collects state across emails.
   - Move idempotency keys into `new IdempotentRequestOptions { IdempotencyKey = ... }`. Attachments take raw bytes or `EmailAttachment.FromBase64(...)`.
   - Team API: use the same client, typed query records instead of `RequestOptions.Query` dictionaries, and the renamed methods from the guide.
   - Exceptions: switch to the 2.0 classes (`LettermintApiException`, `ValidationException`, `RateLimitException`, `LettermintTimeoutException`, …).
   - Enums are structs now: replace `case MessageStatus.Delivered:` with `==` comparisons or `switch` on `ToString()`, and keep a default branch for values the API adds later.
   - Webhooks: `new Webhook(secret).Verify(rawBody, request.Headers)`. Keep passing the raw request body, keep the secret's `whsec_` prefix, and make sure the `X-Lettermint-Signature` and `X-Lettermint-Delivery` headers reach the handler.
   - Rename types using the guide's type-name table.
5. Run `dotnet build` with warnings as errors where the project uses them, and `dotnet test`, and fix every error. Don't send real email or call the live API while testing.
6. Finish with a summary: the files you changed, anything you could not migrate with certainty, and behaviour changes I should review.

Never print, log or commit API tokens or webhook secrets.
````

## Create the client

`LettermintClient.Email()`, `LettermintClient.Api()`, `EmailClient`, `ApiClient` and `ClientOptions` are removed. `LettermintClient` is now the client itself.

```csharp
// 1.x
using var email = LettermintClient.Email(sendingToken, new ClientOptions { Timeout = TimeSpan.FromSeconds(10) });
using var api = LettermintClient.Api(teamToken);

// 2.0
using var lettermint = new LettermintClient(new LettermintOptions
{
    SendingToken = Environment.GetEnvironmentVariable("LETTERMINT_PROJECT_TOKEN"), // lettermint.Emails
    TeamToken = Environment.GetEnvironmentVariable("LETTERMINT_TEAM_TOKEN"),       // the Team API
    Timeout = TimeSpan.FromSeconds(10),
});
```

Pass one token or both. With only one token, calling a part that needs the other throws `LettermintConfigException` (for example `Domains.ListAsync needs TeamToken; …`) before any request.

You can also pass a token string. The SDK chooses the token type by its format:

```csharp
var lettermint = new LettermintClient("lm_team_..."); // team token
var lettermint = new LettermintClient("lm_...");      // project sending token
var lettermint = new LettermintClient(token, new LettermintOptions { Timeout = TimeSpan.FromSeconds(10) });
```

Any other format (SSO tokens, OAuth tokens, an empty string) throws `LettermintConfigException`. Use `SendingToken` or `TeamToken` for those. Setting a token in the options as well as passing one as the first argument also throws.

| 1.x `ClientOptions` | 2.0 `LettermintOptions` |
| --- | --- |
| `BaseUrl` (`Uri`) | `BaseUrl` (`Uri`), unchanged |
| `Timeout` | `Timeout`, unchanged; it now also covers reading the body |
| `HttpClient` | `HttpClient`, unchanged: still owned by you, its handler must not follow redirects, and it must not have default auth headers |
| token argument of `Email()` / `Api()` | `SendingToken` / `TeamToken` |

The SDK-owned `HttpClient` is disposed by `lettermint.Dispose()`, as in 1.x.

## Send an email

In 1.x, `From()` and `Compose()` returned a mutable builder that kept its values after a send, including the idempotency key. In 2.0, `Emails.Compose()` returns an immutable builder: each setter returns a new builder and leaves the old one unchanged. Chaining works as before. If you built an email over several statements, assign the result of each setter.

```csharp
// 1.x
var result = await email.From("Acme <hello@acme.com>")
    .To("jane@example.com")
    .Subject("Welcome")
    .Html("<p>Hi Jane</p>")
    .IdempotencyKey("welcome-jane")
    .SendAsync();

// 2.0: builder
var result = await lettermint.Emails.Compose()
    .From("Acme <hello@acme.com>")
    .To("jane@example.com")
    .Subject("Welcome")
    .Html("<p>Hi Jane</p>")
    .SendAsync(new IdempotentRequestOptions { IdempotencyKey = "welcome-jane" });

// 2.0: a message record
var result = await lettermint.Emails.SendAsync(
    new SendMailRequest { From = "Acme <hello@acme.com>", To = ["jane@example.com"], Subject = "Welcome", Html = "<p>Hi Jane</p>" },
    new IdempotentRequestOptions { IdempotencyKey = "welcome-jane" });
```

```csharp
// 1.x: statements mutated the builder
var builder = email.Compose();
builder.From("hello@acme.com").To("jane@example.com");
if (copy) builder.Cc("team@acme.com");
await builder.Subject("Hi").SendAsync();

// 2.0: keep the returned builder
var draft = lettermint.Emails.Compose().From("hello@acme.com").To("jane@example.com");
if (copy) draft = draft.Cc("team@acme.com");
await draft.Subject("Hi").SendAsync();
```

A base builder can now be shared and reused safely, also across concurrent requests.

### Changed builder methods

| 1.x | 2.0 |
| --- | --- |
| `email.From(x)`, `email.Compose()` | `lettermint.Emails.Compose().From(x)`, `lettermint.Emails.Compose()` |
| Setters change the builder and return it | Setters return a new builder |
| `.IdempotencyKey(key).SendAsync(ct)` | `.SendAsync(new IdempotentRequestOptions { IdempotencyKey = key }, ct)` |
| `.Attach(filename, base64, contentId?, contentType?)` | `.Attach(filename, bytes, contentType?, contentId?)` or `.Attach(EmailAttachment.FromBase64(filename, base64) with { ContentType = …, ContentId = … })` |
| `.Tags(params MessageTag[])`, `.Tags(params SendMailRequestTagsItem[])` | `.Tags(params MessageTagInput[])` or `.Tags(IEnumerable<MessageTagInput>)` |
| `.Tags()` / `.Tag()` threw `ArgumentException` | Throw `LettermintValidationException` (with `Field`); the builder stays unchanged |
| `.Html(null)`, `.Text(null)`, `.Route(null)` | Unchanged: `null` removes the field; so do `Tag(null)` and `ScheduledAt(null)` |
| `.ScheduledAt(string)` | `.ScheduledAt(string?)` or `.ScheduledAt(DateTimeOffset)` |
| `.Settings(settings)`, `.SandboxResult(result)` | Unchanged; both accept `null` to remove the field |
| `.SendAsync(ct)` returns `SendEmailResponse` | `.SendAsync(options?, ct)` returns `SendMailResponse`; the builder can be sent again |
| — | `.Build()` returns the message as a `SendMailRequest` |

Unchanged setters: `To`, `Cc`, `Bcc`, `ReplyTo` (each replaces its list), `Subject`, `Headers`, `Metadata`, `Tag`.

`MessageTag` (the hand-written tag record that validated in its constructor) is removed. Use `new MessageTagInput { Name = …, Value = … }`; tags are validated when you set them on a builder and before every send. `MessageValidation.ValidateTags(tags, hasLegacyTag)` runs the check on its own. `MessageTag` is now the generated type of tags in responses.

### Message records and optional fields

`SendMailRequest` and the other request types are records now (`init` properties, `required` for required fields). Optional fields that the API accepts as `null` are `Optional<T?>` instead of `OptionalNullable<T>`:

```csharp
// 1.x
var settings = new UpdateRouteSettingsData { TrackOpens = true };
var clear = new UpdateWebhookData { BasicAuth = OptionalNullable<WebhookBasicAuthData>.Null };

// 2.0
var settings = new UpdateRouteSettingsData { TrackOpens = true };
var clear = new UpdateWebhookData { BasicAuth = null };  // sends "basic_auth": null
var keep = new UpdateWebhookData();                       // leaves basic_auth out
```

Read an `Optional<T>` with `.IsSet` and `.Value`. `SendMailRequest.Html`, `Text` and `Tag` are `Optional<string?>`: assigning a string works as before, and reading needs `.Value`.

The `JsonElement` payload overloads (`SendAsync(JsonElement)`, `CreateAsync(JsonElement)`, …) are removed. To send a field the SDK does not know yet, put it in the record's `AdditionalProperties`.

## Batch sending, ping, reschedule and cancel

```csharp
// 1.x
await email.SendBatchAsync(new SendMailRequest[] { message1, message2 }, new RequestOptions { IdempotencyKey = "batch-1" });
await email.SendBatchAsync(new List<SendBatchMailRequestItem> { item1 });
await email.PingAsync();
await api.PingAsync();
await email.RescheduleAsync(messageId, new RescheduleMessageRequest { ScheduledAt = "tomorrow at 10am" });
await email.CancelAsync(messageId);

// 2.0
await lettermint.Emails.SendBatchAsync([message1, message2], new IdempotentRequestOptions { IdempotencyKey = "batch-1" });
await lettermint.Emails.SendBatchAsync([builder1, builder2]); // builders work too
await lettermint.Emails.PingAsync(); // sending token
await lettermint.PingAsync();        // team token if configured, otherwise the sending token
await lettermint.Messages.RescheduleAsync(messageId, new RescheduleMessageRequest { ScheduledAt = "tomorrow at 10am" });
await lettermint.Messages.CancelAsync(messageId);
```

`SendBatchAsync` returns `IReadOnlyList<SendMailResponse>` (1.x: `List<SendBatchEmailResponseItem>`). `Messages.RescheduleAsync` and `Messages.CancelAsync` accept either token: the team token when configured, otherwise the sending token, so a sending-only client can still reschedule and cancel what it sent.

## Team API

The endpoint classes move from `LettermintClient.Api(token).X` to `lettermint.X`. Query parameters are typed records instead of `RequestOptions.Query` dictionaries. Every list also has an `IterateAsync()` method that follows `next_cursor`.

```csharp
// 1.x
using var api = LettermintClient.Api(teamToken);
var page = await api.Domains.ListAsync(new RequestOptions
{
    Query = new Dictionary<string, string> { ["page[size]"] = "10", ["filter[status]"] = "verified" },
});

// 2.0
var page = await lettermint.Domains.ListAsync(new ListDomainsQuery { PageSize = 10, FilterStatus = DomainStatus.Verified });
await foreach (var domain in lettermint.Domains.IterateAsync(new ListDomainsQuery { FilterStatus = DomainStatus.Verified }))
{
    Console.WriteLine(domain.Domain);
}
```

| 1.x (`api = LettermintClient.Api(token)`) | 2.0 (`lettermint = new LettermintClient(options)`) |
| --- | --- |
| `api.PingAsync()` | `lettermint.PingAsync()` |
| `api.BlockedFileTypesAsync()` | `lettermint.BlockedFileTypesAsync()` |
| `api.AnalyticsAsync(V1AnalyticsRequest)` | `lettermint.AnalyticsAsync(AnalyticsQuery)` |
| `api.Domains.ListAsync(options)` | `lettermint.Domains.ListAsync(query?)`, `lettermint.Domains.IterateAsync(query?)` |
| `api.Domains.CreateAsync(payload)` | `lettermint.Domains.CreateAsync(payload)` |
| `api.Domains.RetrieveAsync(id)` | `lettermint.Domains.RetrieveAsync(id, query?)` (`Include = [GetDomainQueryIncludeItem.DnsRecords]`) |
| `api.Domains.DeleteAsync(id)` | `lettermint.Domains.DeleteAsync(id)` |
| `api.Domains.VerifyDnsRecordsAsync(id)` | `lettermint.Domains.VerifyDnsRecordsAsync(id)` |
| `api.Domains.VerifyDnsRecordAsync(id, recordId)` | `lettermint.Domains.VerifyDnsRecordAsync(id, recordId)` |
| `api.Domains.UpdateProjectsAsync(id, payload)` | `lettermint.Domains.UpdateProjectsAsync(id, payload)` |
| `api.Messages.ListAsync(options)` | `lettermint.Messages.ListAsync(query?)`, `lettermint.Messages.IterateAsync(query?)` |
| `api.Messages.RetrieveAsync(id)` | `lettermint.Messages.RetrieveAsync(id)` |
| `api.Messages.EventsAsync(id)` | `lettermint.Messages.EventsAsync(id, query?)`, `lettermint.Messages.IterateEventsAsync(id, query?)` |
| `api.Messages.SourceAsync(id)` / `HtmlAsync(id)` / `TextAsync(id)` | unchanged, on `lettermint.Messages` |
| `api.Messages.RescheduleAsync(id, payload)` | `lettermint.Messages.RescheduleAsync(id, payload)` |
| `api.Messages.CancelAsync(id)` | `lettermint.Messages.CancelAsync(id)` |
| `api.Messages.ProcessAsync(id, new RequestOptions { IdempotencyKey })` | `lettermint.Messages.ProcessAsync(id, new IdempotentRequestOptions { IdempotencyKey })` |
| `api.Projects.ListAsync(options)` | `lettermint.Projects.ListAsync(query?)`, `lettermint.Projects.IterateAsync(query?)` |
| `api.Projects.CreateAsync(payload)` | `lettermint.Projects.CreateAsync(payload)` |
| `api.Projects.RetrieveAsync(id)` | `lettermint.Projects.RetrieveAsync(id, query?)` |
| `api.Projects.UpdateAsync(id, payload)` | `lettermint.Projects.UpdateAsync(id, payload)` |
| `api.Projects.DeleteAsync(id)` | `lettermint.Projects.DeleteAsync(id)` |
| `api.Projects.RotateTokenAsync(id)` | `lettermint.Projects.RotateTokenAsync(id)` (marked `[Obsolete]`: the API calls it legacy) |
| `api.Projects.RetrieveReportForwardingAsync(id)` | `lettermint.Projects.ReportForwarding.RetrieveAsync(id)` |
| `api.Projects.UpdateReportForwardingAsync(id, payload)` | `lettermint.Projects.ReportForwarding.UpdateAsync(id, payload)` |
| `api.Projects.DeleteReportForwardingAsync(id)` | `lettermint.Projects.ReportForwarding.DeleteAsync(id)` |
| `api.Projects.VerifyReportForwardingAsync(id, payload)` | `lettermint.Projects.ReportForwarding.VerifyAsync(id, payload)` |
| `api.Projects.ResendReportForwardingCodeAsync(id)` | `lettermint.Projects.ReportForwarding.ResendCodeAsync(id)` |
| `api.Routes.ListAsync(projectId, options)` | `lettermint.Routes.ListAsync(projectId, query?)`, `lettermint.Routes.IterateAsync(projectId, query?)` |
| `api.Routes.CreateAsync(projectId, payload)` | `lettermint.Routes.CreateAsync(projectId, payload)` |
| `api.Routes.RetrieveAsync(id)` | `lettermint.Routes.RetrieveAsync(id, query?)` |
| `api.Routes.UpdateAsync(id, payload)` | `lettermint.Routes.UpdateAsync(id, payload)` |
| `api.Routes.DeleteAsync(id)` | `lettermint.Routes.DeleteAsync(id)` |
| `api.Routes.VerifyInboundDomainAsync(id)` | `lettermint.Routes.VerifyInboundDomainAsync(id)` |
| `api.Stats.RetrieveAsync(options with from/to query)` | `lettermint.Stats.RetrieveAsync(new GetStatsQuery { From, To, ProjectId?, IncludeMachine? })` |
| `api.Suppressions.ListAsync(options)` | `lettermint.Suppressions.ListAsync(query?)`, `lettermint.Suppressions.IterateAsync(query?)` |
| `api.Suppressions.CreateAsync(payload)` | `lettermint.Suppressions.CreateAsync(payload)` |
| `api.Suppressions.DeleteAsync(id)` | `lettermint.Suppressions.DeleteAsync(id)` |
| `api.Team.RetrieveAsync()` | `lettermint.Team.RetrieveAsync(query?)` (`Include = [GetTeamQueryIncludeItem.Features]`) |
| `api.Team.UpdateAsync(payload)` | `lettermint.Team.UpdateAsync(payload)` |
| `api.Team.UsageAsync()` | `lettermint.Team.UsageAsync()` |
| `api.Team.RolesAsync()` | `lettermint.Team.RolesAsync()` |
| `api.Team.MembersAsync(options)` | `lettermint.Team.Members.ListAsync(query?)`, `lettermint.Team.Members.IterateAsync(query?)` |
| `api.Team.RetrieveMemberAsync(userId)` | `lettermint.Team.Members.RetrieveAsync(userId)` |
| `api.Team.UpdateMemberAssignmentAsync(userId, payload)` | `lettermint.Team.Members.UpdateAssignmentAsync(userId, payload)` |
| `api.Webhooks.ListAsync(options)` | `lettermint.Webhooks.ListAsync(query?)`, `lettermint.Webhooks.IterateAsync(query?)` |
| `api.Webhooks.CreateAsync(payload)` | `lettermint.Webhooks.CreateAsync(payload)` |
| `api.Webhooks.RetrieveAsync(id)` | `lettermint.Webhooks.RetrieveAsync(id)` |
| `api.Webhooks.UpdateAsync(id, payload)` | `lettermint.Webhooks.UpdateAsync(id, payload)` |
| `api.Webhooks.DeleteAsync(id)` | `lettermint.Webhooks.DeleteAsync(id)` |
| `api.Webhooks.TestAsync(id)` | `lettermint.Webhooks.TestAsync(id)` |
| `api.Webhooks.RegenerateSecretAsync(id)` | `lettermint.Webhooks.RegenerateSecretAsync(id)` |
| `api.Webhooks.DeliveriesAsync(id, options)` | `lettermint.Webhooks.Deliveries.ListAsync(id, query?)`, `lettermint.Webhooks.Deliveries.IterateAsync(id, query?)` |
| `api.Webhooks.ShowDeliveryAsync(id, deliveryId)` | `lettermint.Webhooks.Deliveries.RetrieveAsync(id, deliveryId)` |

Every method takes `RequestOptions? options` (now with a per-call `Timeout`) and a `CancellationToken` last, as before. `RequestOptions.Query` and `RequestOptions.Headers` are removed; `RequestOptions.IdempotencyKey` moved to `IdempotentRequestOptions`, which `Emails.SendAsync`, `Emails.SendBatchAsync` and `Messages.ProcessAsync` take.

### Query parameters

| 1.x `RequestOptions.Query` | 2.0 query record |
| --- | --- |
| `["page[size]"] = "30", ["page[cursor]"] = c` | `new ListDomainsQuery { PageSize = 30, PageCursor = c }` |
| `["filter[status]"] = "verified"` | `FilterStatus = DomainStatus.Verified` |
| `["sort"] = "-created_at,domain"` | `Sort = [ListDomainsQuerySortItem.CreatedAtDescending, ListDomainsQuerySortItem.Domain]` |
| `["filter[tags][0][name]"] = "a", ["filter[tags][0][value]"] = "b"` | `Filter = new ListMessagesQueryFilter { Tags = [new() { Name = "a", Value = "b" }] }` |
| `["filter[enabled]"] = "true"` | `FilterEnabled = true` (sent as `1`) |
| webhooks: `["cursor"] = c` | `Cursor = c` (these lists use `cursor`, not `page[cursor]`) |

Properties are named after the wire names (`page[size]` → `PageSize`, `filter[from_date]` → `FilterFromDate`).

### Paths and pages

IDs are still URL-encoded. An empty or null ID, `.` or `..` now throws `LettermintConfigException` (1.x: `ArgumentException`) before the request.

List responses are `CursorPage<T>`: every named page type (`ListDomainsResponse`, `ListMessagesResponse`, …) derives from `CursorPage<T>` with `Data`, `NextCursor`, `PerPage` and the other cursor fields. 1.x documented a `Meta` object on message lists; read `page.NextCursor`, or use `IterateAsync()`.

### Response changes

- `Suppressions.DeleteAsync` returns `DeleteSuppressionResponse` (one record for HTTP 200 and 202). Check `Status`: `review_ticket_created` and `review_ticket_exists` mean the suppression was not removed yet.
- `Emails.SendAsync` returns `SendMailResponse`, with `Status` (`Pending` or `Scheduled`) and `ScheduledAt`.
- `Projects.ReportForwarding.DeleteAsync` returns `Task` (HTTP 204), as before.
- Text endpoints (`SourceAsync`, `HtmlAsync`, `TextAsync`) still return the raw string; both ping methods still return the trimmed `pong`.

## Errors

`LettermintException` stays the base class. `LettermintApiException` stays the class of HTTP errors, with subclasses per status. Timeouts, network failures and decoding failures, which were all a plain `LettermintException` in 1.x, have their own classes.

| Situation | 1.x | 2.0 |
| --- | --- | --- |
| HTTP 400 and other 4xx | `LettermintApiException` (`StatusCode`, `ResponseBody`) | `LettermintApiException` (`StatusCode`, `Code`, `Message`, `Details`, `Body`) |
| HTTP 401 | `LettermintApiException` | `AuthenticationException` |
| HTTP 403 | `LettermintApiException` | `PermissionException` |
| HTTP 404 | `LettermintApiException` | `NotFoundException` |
| HTTP 409 | `LettermintApiException` | `ConflictException` |
| HTTP 422 | `LettermintApiException` | `ValidationException` (`Errors`) |
| HTTP 429 | `LettermintApiException` | `RateLimitException` (`RetryAfter`) |
| HTTP 5xx | `LettermintApiException` | `ServerException` |
| Empty or invalid JSON body, HTML error page | `LettermintException` ("invalid JSON response") or `LettermintApiException` with the HTML | `UnexpectedResponseException` (`StatusCode`, `BodyExcerpt`) |
| Redirect (3xx) | `LettermintApiException` (the default handler did not follow it) | `RedirectException` (`StatusCode`); also thrown when a caller-supplied `HttpClient` followed one |
| Timeout | `LettermintException` ("The request timed out.") | `LettermintTimeoutException` (`Timeout`); covers headers and body |
| Network failure | `LettermintException` ("The HTTP request failed.") | `ConnectionException` (`InnerException`) |
| Caller cancellation | `OperationCanceledException` | `OperationCanceledException`, unchanged |
| An unknown enum value in a response | `LettermintException` (JSON error "Unknown API enum value.") | Decodes; the value keeps its raw string |
| Invalid tags | `ArgumentException` | `LettermintValidationException` (`Field`) |
| Missing or wrong token, bad option, bad ID | `ArgumentException` | `LettermintConfigException` |
| Webhook verification | `WebhookVerificationException` | `WebhookVerificationException` (`Reason`, `ReasonCode`) |

Property changes: `ResponseBody` (a token-redacted string) is replaced by `Body` (the decoded JSON as a `JsonElement?`), plus `Code`, `Details` and the API's `Message` (1.x: `Lettermint returned HTTP <status>.`).

```csharp
// 1.x
catch (LettermintApiException error) when (error.StatusCode == HttpStatusCode.TooManyRequests)
{
    Console.WriteLine(error.ResponseBody);
}

// 2.0
catch (RateLimitException error)
{
    await Task.Delay(error.RetryAfter ?? TimeSpan.FromSeconds(1));
}
catch (ValidationException error)
{
    Console.WriteLine($"{error.Code} {error.Message}");
}
```

The SDK does not retry requests. Pass an idempotency key when you retry a send.

## Webhooks

`Webhook` is an instance class now. `Verify` takes the raw body and the request headers, requires both `X-Lettermint-Signature` and `X-Lettermint-Delivery`, and returns a typed `WebhookPayload` instead of a `JsonElement`. The old signature-only check is `VerifySignature`.

```csharp
// 1.x
JsonElement payload = Webhook.Verify(rawBody, signatureHeader, secret, toleranceSeconds: 300);

// 2.0
var webhook = new Webhook(secret);                                   // keep it, for example as a singleton
WebhookPayload payload = webhook.Verify(rawBody, request.Headers);    // IHeaderDictionary, HttpHeaders, dictionaries
WebhookPayload payload2 = webhook.VerifySignature(rawBody, signatureHeader, deliveryHeader);
```

- The body may be a `string` or bytes (`byte[]`, `ReadOnlySpan<byte>`).
- The tolerance is a `TimeSpan` (default 300 seconds) and applies in both directions. `TimeSpan.Zero` accepts only the current second; in 1.x, `0` disabled the check.
- The clock is the third constructor argument (`TimeProvider`), as before.
- `payload.Event` is a `WebhookEvent`, `payload.Data` a `JsonElement`; other fields are in `AdditionalProperties`, and `GetData<T>()` deserializes `Data`.
- `WebhookVerificationException.Reason` tells why verification failed: `SignatureHeaderMissing`, `SignatureHeaderMalformed`, `DeliveryHeaderMissing`, `DeliveryTimestampMismatch`, `TimestampOutOfTolerance`, `SignatureMismatch`, `BodyInvalid` or `PayloadInvalid` (`ReasonCode`: the snake_case codes of the other SDKs).
- An empty secret or a negative tolerance throws `LettermintConfigException` (1.x: `WebhookVerificationException` or `ArgumentOutOfRangeException`).

## Type names

The types are generated from the API specification of lettermint#2582 and use its names. They stay in the `Lettermint.Models` namespace. Types that are not listed below keep their name. Some shapes also changed:

- Generated types are `sealed partial record` types with `init` properties instead of classes with `get; set;` properties. Required fields use the `required` modifier, so object initializers must set them. Unknown fields are still kept in `AdditionalProperties`, which is an `IDictionary<string, JsonElement>?`.
- Lists are `IReadOnlyList<T>` (1.x: `List<T>`), maps are `IReadOnlyDictionary<string, T>` (1.x: `Dictionary<string, T>`). Collection expressions (`To = ["a@example.com"]`) work for both.
- Enums are open `readonly struct` types instead of C# `enum` types. Static values keep their names (`MessageStatus.Delivered`, `TlsPolicy.Enforced`), but they are not constants: `case MessageStatus.Delivered:` no longer compiles. Use `==`, `if`, `switch` on `ToString()`, or `when` clauses, and handle values the API adds later. `RecordType.TXT`, `RecordType.CNAME` and `RecordType.MX` are `RecordType.Txt`, `RecordType.Cname` and `RecordType.Mx`.
- `ApiModel` (the base class with `ToJson()`) is removed; serialize with `System.Text.Json` directly.
- `OptionalNullable<T>` is `Optional<T?>`; `OptionalNullable<T>.Null` is `null`.
- One type for tags in responses (`MessageTag`) and one for tags you send (`MessageTagInput`).
- `SendMailResponse` is one record for pending and scheduled sends.
- `ToString()` of a record that holds a secret returned by the API (`ProjectCreatedData.ApiToken`, `RotateProjectTokenResponse.NewToken`, `WebhookSecretData.Secret`, `WebhookBasicAuthData.Password`) shows `[redacted]` for it; the property itself still returns the value.

| 1.x | 2.0 |
| --- | --- |
| `CancelScheduledMessageResponse` | `ScheduledMessage` |
| `DomainDestroyResponse` | `MessageResponse` |
| `DomainIndexResponse` | `ListDomainsResponse` |
| `DomainUpdateProjectsResponse` | `DomainMutationResponse` |
| `DomainVerifyDnsRecordsResponse` | `DnsVerificationSuccessResponse` |
| `DomainVerifyDnsRecordsResponseRecommendedFailedRecordsItem` | `DnsVerificationFailedRecord` |
| `DomainVerifySpecificDnsRecordResponse` | `MessageResponse` |
| `MessageDataTagsItem` | `MessageTag` |
| `MessageEventDataTagsItem` | `MessageTag` |
| `MessageEventsResponse` | `ListMessageEventsResponse` |
| `MessageIndexResponse` | `ListMessagesResponse` |
| `MessageListDataTagsItem` | `MessageTag` |
| `ProcessInboundMessageResponseData` | `ProcessInboundMessageResult` |
| `ProjectDestroyResponse` | `MessageResponse` |
| `ProjectIndexResponse` | `ListProjectsResponse` |
| `ProjectRotateTokenResponse` | `RotateProjectTokenResponse` |
| `ProjectStoreResponse` | `ProjectCreatedData` |
| `ProjectUpdateResponse` | `ProjectMutationResponse` |
| `RescheduleMessageResponse` | `ScheduledMessage` |
| `RouteDestroyResponse` | `MessageResponse` |
| `RouteIndexResponse` | `ListRoutesResponse` |
| `RouteStoreResponse` | `RouteMutationResponse` |
| `RouteUpdateResponse` | `RouteMutationResponse` |
| `RouteVerifyInboundDomainResponse` | `InboundDomainVerificationResponse` |
| `RouteVerifyInboundDomainResponseData` | `InboundDomainVerification` |
| `SendBatchEmailResponseItem` | `SendMailResponse` |
| `SendBatchMailRequestItem` | `SendMailRequest` |
| `SendBatchMailRequestItemAttachmentsItem` | `MessageAttachmentInput` |
| `SendBatchMailRequestItemSettings` | `SendMailRequestSettings` |
| `SendBatchMailRequestItemTagsItem` | `MessageTagInput` |
| `SendEmailResponse` | `SendMailResponse` |
| `SendMailRequestAttachmentsItem` | `MessageAttachmentInput` |
| `SendMailRequestTagsItem` | `MessageTagInput` |
| `SuppressionDestroyResponse` | `DeleteSuppressionResponse` |
| `SuppressionIndexResponse` | `ListSuppressionsResponse` |
| `SuppressionStoreResponseData` | `SuppressionStoreResult` |
| `TeamMembersResponse` | `ListTeamMembersResponse` |
| `TeamRolesResponse` | `TeamRoleListResponse` |
| `TeamUpdateResponse` | `TeamMutationResponse` |
| `V1AnalyticsRequest` | `AnalyticsQuery` |
| `V1AnalyticsRequestCompare` | `AnalyticsComparison` |
| `V1AnalyticsRequestFiltersItem` | `AnalyticsFilter` |
| `V1AnalyticsRequestFiltersItemOperator` | `AnalyticsFilterOperator` |
| `V1AnalyticsRequestIncludeItem` | `AnalyticsSection` |
| `V1AnalyticsRequestInterval` | `AnalyticsInterval` |
| `V1AnalyticsRequestMetricsItem` | `AnalyticsMetric` |
| `V1AnalyticsRequestSort` | `AnalyticsSort` |
| `V1AnalyticsRequestSortDirection` | `AnalyticsSortDirection` |
| `V1AnalyticsRequestSortMetric` | `AnalyticsMetric` |
| `V1AnalyticsResponse` | `AnalyticsResponse` |
| `V1AnalyticsResponseData` | `AnalyticsResults` |
| `V1AnalyticsResponseDataBreakdownItem` | `AnalyticsBreakdownRow` |
| `V1AnalyticsResponseDataBreakdownItemChangeValue` | `AnalyticsMetricChange` |
| `V1AnalyticsResponseDataBreakdownItemMetrics` | `AnalyticsMetricValues` |
| `V1AnalyticsResponseDataBreakdownItemPrevious` | `AnalyticsComparisonValues` |
| `V1AnalyticsResponseDataBreakdownItemPreviousMetrics` | `AnalyticsMetricValues` |
| `V1AnalyticsResponseDataBreakdownItemPreviousRateBases` | `AnalyticsRateBases` |
| `V1AnalyticsResponseDataBreakdownItemPreviousRateBasesBounceRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemPreviousRateBasesComplaintRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemPreviousRateBasesDeferralRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemPreviousRateBasesDeliveryRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemPreviousRateBasesEffectiveDeliveryRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemPreviousRateBasesHumanClickRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemPreviousRateBasesHumanOpenRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemRateBases` | `AnalyticsRateBases` |
| `V1AnalyticsResponseDataBreakdownItemRateBasesBounceRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemRateBasesComplaintRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemRateBasesDeferralRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemRateBasesDeliveryRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemRateBasesEffectiveDeliveryRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemRateBasesHumanClickRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemRateBasesHumanOpenRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemTrendItem` | `AnalyticsTimeSeriesPoint` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemChangeValue` | `AnalyticsMetricChange` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemMetrics` | `AnalyticsMetricValues` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemPrevious` | `AnalyticsComparisonValues` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemPreviousMetrics` | `AnalyticsMetricValues` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemPreviousRateBases` | `AnalyticsRateBases` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemPreviousRateBasesBounceRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemPreviousRateBasesComplaintRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemPreviousRateBasesDeferralRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemPreviousRateBasesDeliveryRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemPreviousRateBasesEffectiveDeliveryRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemPreviousRateBasesHumanClickRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemPreviousRateBasesHumanOpenRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemRateBases` | `AnalyticsRateBases` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemRateBasesBounceRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemRateBasesComplaintRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemRateBasesDeferralRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemRateBasesDeliveryRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemRateBasesEffectiveDeliveryRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemRateBasesHumanClickRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataBreakdownItemTrendItemRateBasesHumanOpenRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataSummary` | `AnalyticsSummary` |
| `V1AnalyticsResponseDataSummaryChangeValue` | `AnalyticsMetricChange` |
| `V1AnalyticsResponseDataSummaryMetrics` | `AnalyticsMetricValues` |
| `V1AnalyticsResponseDataSummaryPrevious` | `AnalyticsComparisonValues` |
| `V1AnalyticsResponseDataSummaryPreviousMetrics` | `AnalyticsMetricValues` |
| `V1AnalyticsResponseDataSummaryPreviousRateBases` | `AnalyticsRateBases` |
| `V1AnalyticsResponseDataSummaryPreviousRateBasesBounceRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataSummaryPreviousRateBasesComplaintRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataSummaryPreviousRateBasesDeferralRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataSummaryPreviousRateBasesDeliveryRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataSummaryPreviousRateBasesEffectiveDeliveryRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataSummaryPreviousRateBasesHumanClickRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataSummaryPreviousRateBasesHumanOpenRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataSummaryRateBases` | `AnalyticsRateBases` |
| `V1AnalyticsResponseDataSummaryRateBasesBounceRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataSummaryRateBasesComplaintRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataSummaryRateBasesDeferralRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataSummaryRateBasesDeliveryRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataSummaryRateBasesEffectiveDeliveryRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataSummaryRateBasesHumanClickRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataSummaryRateBasesHumanOpenRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataTimeSeriesItem` | `AnalyticsTimeSeriesPoint` |
| `V1AnalyticsResponseDataTimeSeriesItemChangeValue` | `AnalyticsMetricChange` |
| `V1AnalyticsResponseDataTimeSeriesItemMetrics` | `AnalyticsMetricValues` |
| `V1AnalyticsResponseDataTimeSeriesItemPrevious` | `AnalyticsComparisonValues` |
| `V1AnalyticsResponseDataTimeSeriesItemPreviousMetrics` | `AnalyticsMetricValues` |
| `V1AnalyticsResponseDataTimeSeriesItemPreviousRateBases` | `AnalyticsRateBases` |
| `V1AnalyticsResponseDataTimeSeriesItemPreviousRateBasesBounceRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataTimeSeriesItemPreviousRateBasesComplaintRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataTimeSeriesItemPreviousRateBasesDeferralRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataTimeSeriesItemPreviousRateBasesDeliveryRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataTimeSeriesItemPreviousRateBasesEffectiveDeliveryRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataTimeSeriesItemPreviousRateBasesHumanClickRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataTimeSeriesItemPreviousRateBasesHumanOpenRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataTimeSeriesItemRateBases` | `AnalyticsRateBases` |
| `V1AnalyticsResponseDataTimeSeriesItemRateBasesBounceRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataTimeSeriesItemRateBasesComplaintRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataTimeSeriesItemRateBasesDeferralRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataTimeSeriesItemRateBasesDeliveryRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataTimeSeriesItemRateBasesEffectiveDeliveryRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataTimeSeriesItemRateBasesHumanClickRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseDataTimeSeriesItemRateBasesHumanOpenRate` | `AnalyticsRateBase` |
| `V1AnalyticsResponseMeta` | `AnalyticsMeta` |
| `V1AnalyticsResponseMetaAlignment` | `AnalyticsMetaAlignment` |
| `V1AnalyticsResponseMetaCollectionCompleteness` | `AnalyticsMetaCollectionCompleteness` |
| `V1AnalyticsResponseMetaComparison` | `AnalyticsMetaComparison` |
| `V1AnalyticsResponseMetaInterval` | `AnalyticsInterval` |
| `V1AnalyticsResponseMetaTimeBasis` | `AnalyticsMetaTimeBasis` |
| `V1AnalyticsResponsePagination` | `AnalyticsPagination` |
| `V1BlockedFileTypesResponse` | `BlockedFileTypes` |
| `WebhookDeliveriesResponse` | `ListWebhookDeliveriesResponse` |
| `WebhookDestroyResponse` | `MessageResponse` |
| `WebhookIndexResponse` | `ListWebhooksResponse` |
| `WebhookRegenerateSecretResponse` | `WebhookSecretResponse` |
| `WebhookStoreResponse` | `WebhookSecretResponse` |
| `WebhookTestResponse` | `TestWebhookResponse` |
| `WebhookUpdateResponse` | `WebhookMutationResponse` |

### Removed types

lettermint#2582 removed these schemas from the API specification:

| 1.x | 2.0 |
| --- | --- |
| `AnalyticsResponseData` | Removed. Use `AnalyticsResponse` (`Data` is `AnalyticsResults`). |
| `StatsRequestData` | Removed. Use `GetStatsQuery`, the parameters of `Stats.RetrieveAsync()`. |
| `MessageIndexResponseMeta` | Removed. Message lists are flat `CursorPage<MessageListData>` pages. |
| `MessageEventsResponseMeta` | Removed. Event lists are flat `CursorPage<MessageEventData>` pages. |
| `SuppressionStoreResponseMessage1` | Removed; not exported by 1.x. `SuppressionStoreResponse.Message` is a `string`. |

### Removed classes and helpers

| 1.x | 2.0 |
| --- | --- |
| `LettermintClient.Email(token, options)` (static) | `new LettermintClient(new LettermintOptions { SendingToken = token, … }).Emails` |
| `LettermintClient.Api(token, options)` (static) | `new LettermintClient(new LettermintOptions { TeamToken = token, … })` |
| `EmailClient` | `Emails` (`lettermint.Emails`) |
| `ApiClient` | `LettermintClient` |
| `DomainsEndpoint`, `MessagesEndpoint`, `ProjectsEndpoint`, `RoutesEndpoint`, `StatsEndpoint`, `SuppressionsEndpoint`, `TeamEndpoint`, `WebhooksEndpoint` | `Domains`, `Messages`, `Projects` (with `ReportForwarding`), `Routes`, `Stats`, `Suppressions`, `Team` (with `TeamMembers`), `Webhooks` (with `WebhookDeliveries`); use the properties of `LettermintClient` |
| `ClientOptions` | `LettermintOptions` |
| `RequestOptions` (`Query`, `Headers`, `IdempotencyKey`) | `RequestOptions` (`Timeout`), `IdempotentRequestOptions` (`IdempotencyKey`) and typed query records |
| `EmailBuilder` (mutable) | `EmailBuilder` (immutable), from `lettermint.Emails.Compose()` |
| `MessageTag` (hand-written) | `MessageTagInput` |
| `OptionalNullable<T>`, `OptionalNullableConverterFactory` | `Optional<T>` |
| `Webhook.Verify(payload, signature, secret, …)` (static) | `new Webhook(secret, tolerance?, timeProvider?).Verify(body, headers)` or `.VerifySignature(body, signature, delivery?)` |
| `ApiModel` | Removed. Records have `AdditionalProperties`; serialize with `System.Text.Json`. |
| `LettermintJson` | Removed (internal). Use `System.Text.Json` with default options. |
| `JsonUnion<TObject, TArray>`, `JsonUnionConverterFactory` | Removed; no response uses them any more. |
| `WireEnumConverter<T>`, `CollectionResponseConverter<T>`, `ApiDictionaryConverterFactory` | Removed (internal converters). |
| `ApiOperationAttribute` | Removed. |
| `JsonElement` payload overloads of every method | Removed. Use the request records; put unknown fields in `AdditionalProperties`. |
