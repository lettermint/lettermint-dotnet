# Lettermint .NET SDK

[![NuGet](https://img.shields.io/nuget/v/Lettermint?style=flat-square)](https://www.nuget.org/packages/Lettermint)
[![.NET Version](https://img.shields.io/badge/.NET-8%2B-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](LICENSE)
[![Join our Discord server](https://img.shields.io/discord/1305510095588819035?logo=discord&logoColor=eee&label=Discord&labelColor=464ce5&color=0D0E28&cacheSeconds=43200)](https://lettermint.co/r/discord)

The official .NET SDK for [Lettermint](https://lettermint.co). It targets .NET 8 and .NET 10 and has no dependencies.

Upgrading from 1.x? Read [UPGRADE.md](UPGRADE.md).

## Installation

```sh
dotnet add package Lettermint
```

## Quick start

Create a client with a project sending token and send an email:

```csharp
using Lettermint;
using Lettermint.Models;

using var lettermint = new LettermintClient(new LettermintOptions
{
    SendingToken = Environment.GetEnvironmentVariable("LETTERMINT_PROJECT_TOKEN"),
});

var result = await lettermint.Emails.SendAsync(new SendMailRequest
{
    From = "Acme <hello@acme.com>",
    To = ["jane@example.com"],
    Subject = "Welcome to Acme",
    Html = "<p>Thanks for signing up.</p>",
    Text = "Thanks for signing up.",
});

Console.WriteLine($"{result.MessageId} {result.Status}"); // "…", "pending"
```

## Tokens

Lettermint has two kinds of API tokens:

| Option | Token | Used by | Sent as |
| --- | --- | --- | --- |
| `SendingToken` | Project sending token (`lm_…`) | `lettermint.Emails` | `x-lettermint-token` header |
| `TeamToken` | Team API token (`lm_team_…`) | Every other part (domains, messages, projects, …) | `Authorization: Bearer` header |

Pass one or both:

```csharp
var lettermint = new LettermintClient(new LettermintOptions
{
    SendingToken = Environment.GetEnvironmentVariable("LETTERMINT_PROJECT_TOKEN"),
    TeamToken = Environment.GetEnvironmentVariable("LETTERMINT_TEAM_TOKEN"),
});
```

Each part uses its own token and never falls back to the other one. If the token a method needs is missing, the method throws a `LettermintConfigException` that names the option (`Domains.ListAsync needs TeamToken; …`) before any request. `lettermint.PingAsync()` uses the team token when it is set, otherwise the sending token. `Messages.RescheduleAsync` and `Messages.CancelAsync` accept either token in the same way.

You can also pass a single token as a string. The SDK chooses its type by the format: `lm_team_` followed by letters and digits is a team token, and `lm_` followed by letters and digits is a sending token. Any other value, such as an SSO verification token (`lm_sso_…`), throws a `LettermintConfigException`; set `SendingToken` or `TeamToken` explicitly in that case. Error messages never contain the token.

```csharp
var lettermint = new LettermintClient(Environment.GetEnvironmentVariable("LETTERMINT_TOKEN")!);
// Other options go in the second argument (leave its token properties unset):
var withTimeout = new LettermintClient(token, new LettermintOptions { Timeout = TimeSpan.FromSeconds(10) });
```

### Options

| Option | Default | Description |
| --- | --- | --- |
| `SendingToken` | | Project sending token. |
| `TeamToken` | | Team API token. |
| `BaseUrl` | `https://api.lettermint.co/v1` | API base URL. |
| `Timeout` | 30 seconds | Request timeout. It covers the response headers and the body. |
| `HttpClient` | an SDK-owned client | An `HttpClient` to send requests with (see below). |

The client holds no per-request state and is thread-safe: create it once and share it, for example as a singleton. `Dispose()` disposes the `HttpClient` the SDK created; a caller-supplied `HttpClient` stays yours.

### HttpClient and dependency injection

By default the SDK uses its own `HttpClient` with a `SocketsHttpHandler { AllowAutoRedirect = false }`, so a redirect can never carry a token to another host. You can pass your own `HttpClient`, for example one from `IHttpClientFactory`. Its handler **must not follow redirects**, and it must not have default `Authorization` or `x-lettermint-token` headers (the SDK rejects those). The SDK still treats every 3xx as an error, and throws a `RedirectException` when it detects that your handler followed a redirect, but by then the request has already gone to the other location.

```csharp
builder.Services.AddHttpClient("lettermint")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

builder.Services.AddSingleton(services => new LettermintClient(new LettermintOptions
{
    SendingToken = builder.Configuration["Lettermint:ProjectToken"],
    TeamToken = builder.Configuration["Lettermint:TeamToken"],
    HttpClient = services.GetRequiredService<IHttpClientFactory>().CreateClient("lettermint"),
}));
```

The SDK does not ship an `AddLettermint()` extension, so that it has no dependency on `Microsoft.Extensions.*`. The default client (no `HttpClient` option) is fine as a singleton too: it recycles connections every five minutes.

## Sending email

### The email builder

`Emails.Compose()` returns an immutable builder. Every setter returns a new builder and leaves the original unchanged, so you can keep a base builder and reuse it, also across concurrent requests:

```csharp
var welcome = lettermint.Emails.Compose()
    .From("Acme <hello@acme.com>")
    .Subject("Welcome to Acme")
    .Tags(new MessageTagInput { Name = "campaign", Value = "welcome" });

await welcome.To("jane@example.com").Html("<p>Hi Jane</p>").SendAsync();
await welcome.To("john@example.com").Html("<p>Hi John</p>").SendAsync();
```

When you build an email over several statements, keep the returned builder:

```csharp
var email = lettermint.Emails.Compose().From("hello@acme.com").To(user.Email).Subject("Your invoice");
if (user.Accountant is not null) email = email.Cc(user.Accountant);
await email.Html(invoiceHtml).SendAsync();
```

| Method | Description |
| --- | --- |
| `From(address)` | Sender, for example `Acme <hello@acme.com>`. |
| `To(...)`, `Cc(...)`, `Bcc(...)`, `ReplyTo(...)` | Replace the recipient list. |
| `Subject(text)` | Subject line. |
| `Html(html?)`, `Text(text?)` | Bodies. `null` removes one. |
| `Headers(dictionary)` | Custom email headers (not HTTP headers). |
| `Metadata(dictionary)` | Data stored with the message, not added as headers. |
| `Tags(...)`, `Tag(name?)` | Name/value tags, and the legacy single tag. |
| `Route(slug?)` | The route to send through. |
| `ScheduledAt(DateTimeOffset)`, `ScheduledAt(string?)` | Delivery time: an instant, ISO 8601, or English such as `tomorrow 9am`. |
| `Settings(SendMailRequestSettings?)` | Per-email settings (`TrackOpens`, `TrackClicks`, `Tls`) that override the route. |
| `SandboxResult(result?)` | The result a Sandbox project simulates. |
| `Attach(...)` | Adds an attachment. |
| `SendAsync(options?, cancellationToken?)` | Sends a snapshot of the email. The builder can be sent again. |
| `Build()` | Returns the message as a `SendMailRequest`. |

`Emails.Compose(message)` starts a builder from an existing `SendMailRequest`.

### Messages as objects

`Emails.SendAsync()` takes a `SendMailRequest`, a generated record that uses the API's fields:

```csharp
await lettermint.Emails.SendAsync(new SendMailRequest
{
    From = "Acme <hello@acme.com>",
    To = ["jane@example.com"],
    ReplyTo = ["support@acme.com"],
    Subject = "Your order has shipped",
    Html = html,
    Metadata = new Dictionary<string, string> { ["order_id"] = "1234" },
});
```

Records are immutable; use `with` to derive one message from another: `message with { Subject = "Reminder" }`.

### Batch sending

Send up to 500 emails in one request. Pass messages or builders:

```csharp
IReadOnlyList<SendMailResponse> results = await lettermint.Emails.SendBatchAsync(
[
    new SendMailRequest { From = "hello@acme.com", To = ["jane@example.com"], Subject = "Hi Jane", Text = "Hello" },
    welcome.To("john@example.com").Html("<p>Hi John</p>").Build(),
]);
```

### Idempotency

Pass an idempotency key to make retries safe. The API processes a key once, so a retry with the same key does not send the email again. The key applies only to the call it is passed to.

```csharp
var once = new IdempotentRequestOptions { IdempotencyKey = $"order-{order.Id}-confirmation" };
await lettermint.Emails.SendAsync(message, once);
await builder.SendAsync(new IdempotentRequestOptions { IdempotencyKey = "welcome-jane" });
await lettermint.Emails.SendBatchAsync(messages, new IdempotentRequestOptions { IdempotencyKey = "newsletter-2026-10" });
```

The SDK never retries on its own.

### Scheduling

```csharp
var result = await lettermint.Emails.Compose()
    .From("hello@acme.com")
    .To("jane@example.com")
    .Subject("Your trial ends tomorrow")
    .Text("…")
    .ScheduledAt(DateTimeOffset.UtcNow.AddDays(1))
    .SendAsync();

if (result.Status == MessageStatus.Scheduled) Console.WriteLine(result.ScheduledAt);

await lettermint.Messages.RescheduleAsync(result.MessageId, new RescheduleMessageRequest { ScheduledAt = "2026-10-20T09:00:00Z" });
await lettermint.Messages.CancelAsync(result.MessageId);
```

### Sandbox

In a Sandbox project, nothing is delivered. Choose the simulated result per email with `.SandboxResult(SandboxResult.HardBounced)`; the response reports `Sandbox` and `SandboxResult`.

### Tags

`Tags()` accepts up to 20 case-sensitive name/value tags (19 when the legacy `Tag()` is also set). Names match `^[A-Za-z0-9_-]{1,32}$`, may not start with `__lettermint` and must be unique. Values match `^[A-Za-z0-9_-]{1,64}$`. The SDK checks this before the request and throws a `LettermintValidationException` with a `Field`. Because builders are immutable, a rejected tag leaves the builder unchanged. `MessageValidation.ValidateTags()` runs the same check on its own.

### Attachments

Pass raw bytes (the SDK base64-encodes them) or base64 text:

```csharp
await lettermint.Emails.Compose()
    .From("billing@acme.com")
    .To("jane@example.com")
    .Subject("Your invoice")
    .Html("<img src=\"cid:logo\"> Your invoice is attached.")
    .Attach("invoice.pdf", await File.ReadAllBytesAsync("invoice.pdf"), contentType: "application/pdf")
    .Attach(EmailAttachment.FromBase64("logo.png", logoBase64) with { ContentId = "logo" })
    .SendAsync();
```

`new EmailAttachment(filename, bytes) { ContentType = …, ContentId = … }` creates an attachment from bytes. In a `SendMailRequest`, attachments are `MessageAttachmentInput` records with base64 `Content`. `lettermint.BlockedFileTypesAsync()` lists the extensions and MIME types the API rejects.

## Team API

With a team token, the client manages domains, messages, projects, routes, statistics, suppressions, the team and webhooks:

```csharp
var lettermint = new LettermintClient(new LettermintOptions { TeamToken = Environment.GetEnvironmentVariable("LETTERMINT_TEAM_TOKEN") });

var domain = await lettermint.Domains.CreateAsync(new StoreDomainData { Domain = "acme.com" });
await lettermint.Domains.VerifyDnsRecordsAsync(domain.Id);

var project = await lettermint.Projects.CreateAsync(new StoreProjectData { Name = "Production" });
Console.WriteLine(project.ApiToken); // the new project's sending token, shown once

var stats = await lettermint.Stats.RetrieveAsync(new GetStatsQuery { From = "2026-10-01", To = "2026-10-31" });
var html = await lettermint.Messages.HtmlAsync("message-id");
```

| Property | Methods |
| --- | --- |
| `Domains` | `ListAsync`, `IterateAsync`, `CreateAsync`, `RetrieveAsync`, `DeleteAsync`, `VerifyDnsRecordsAsync`, `VerifyDnsRecordAsync`, `UpdateProjectsAsync` |
| `Messages` | `ListAsync`, `IterateAsync`, `RetrieveAsync`, `EventsAsync`, `IterateEventsAsync`, `SourceAsync`, `HtmlAsync`, `TextAsync`, `RescheduleAsync`, `CancelAsync`, `ProcessAsync` |
| `Projects` | `ListAsync`, `IterateAsync`, `CreateAsync`, `RetrieveAsync`, `UpdateAsync`, `DeleteAsync`, `RotateTokenAsync` |
| `Projects.ReportForwarding` | `RetrieveAsync`, `UpdateAsync`, `DeleteAsync`, `VerifyAsync`, `ResendCodeAsync` |
| `Routes` | `ListAsync(projectId)`, `IterateAsync(projectId)`, `CreateAsync(projectId, …)`, `RetrieveAsync`, `UpdateAsync`, `DeleteAsync`, `VerifyInboundDomainAsync` |
| `Stats` | `RetrieveAsync` |
| `Suppressions` | `ListAsync`, `IterateAsync`, `CreateAsync`, `DeleteAsync` |
| `Team` | `RetrieveAsync`, `UpdateAsync`, `UsageAsync`, `RolesAsync` |
| `Team.Members` | `ListAsync`, `IterateAsync`, `RetrieveAsync`, `UpdateAssignmentAsync` |
| `Webhooks` | `ListAsync`, `IterateAsync`, `CreateAsync`, `RetrieveAsync`, `UpdateAsync`, `DeleteAsync`, `TestAsync`, `RegenerateSecretAsync` |
| `Webhooks.Deliveries` | `ListAsync(webhookId)`, `IterateAsync(webhookId)`, `RetrieveAsync(webhookId, deliveryId)` |
| (root) | `PingAsync`, `AnalyticsAsync`, `BlockedFileTypesAsync` |

### Query parameters and pagination

Query parameters are typed records. The SDK sends them in the API's bracket syntax (`page[size]=30&filter[status]=verified&sort=-created_at`):

```csharp
var page = await lettermint.Domains.ListAsync(new ListDomainsQuery
{
    PageSize = 30,
    FilterStatus = DomainStatus.Verified,
    Sort = [ListDomainsQuerySortItem.CreatedAtDescending],
});

Console.WriteLine($"{page.Data.Count} {page.NextCursor}");
var next = await lettermint.Domains.ListAsync(new ListDomainsQuery { PageSize = 30, PageCursor = page.NextCursor });
```

Booleans are sent as `1`/`0`, lists of values are comma-separated and lists of objects are indexed (`filter[tags][0][name]=…`). Every list response is a `CursorPage<T>` (`ListDomainsResponse : CursorPage<DomainListData>`). Every list also has an `IterateAsync()` method, an `IAsyncEnumerable<T>` that follows `NextCursor` until the last page:

```csharp
await foreach (var message in lettermint.Messages.IterateAsync(new ListMessagesQuery { FilterStatus = MessageStatus.HardBounced }))
{
    Console.WriteLine($"{message.Id} {message.Subject}");
}
```

Stop early with `break`. The SDK requests the next page only when you get to it, and it stops when the API repeats a cursor.

### Cancellation and timeouts

Every method takes `RequestOptions` (with a per-call `Timeout`) and a `CancellationToken` as its last two arguments:

```csharp
using var cts = new CancellationTokenSource();
var page = await lettermint.Messages.ListAsync(null, new RequestOptions { Timeout = TimeSpan.FromSeconds(5) }, cts.Token);
```

Cancellation throws `OperationCanceledException`, as usual in .NET. A timeout throws `LettermintTimeoutException`.

## Errors

Every exception the SDK throws extends `LettermintException`:

| Class | When | Properties |
| --- | --- | --- |
| `LettermintApiException` | Any 4xx or 5xx JSON (or empty) response | `StatusCode`, `Code`, `Message`, `Details`, `Body` |
| `AuthenticationException` | 401 | |
| `PermissionException` | 403 | |
| `NotFoundException` | 404 | |
| `ConflictException` | 409 | |
| `ValidationException` | 422 | `Errors` (field errors) |
| `RateLimitException` | 429 | `RetryAfter` (`TimeSpan?`) |
| `ServerException` | 5xx | |
| `LettermintTimeoutException` | No complete response within the timeout | `Timeout` |
| `ConnectionException` | The request failed (DNS, TLS, refused, reset) | `InnerException` |
| `UnexpectedResponseException` | An empty or non-JSON body where JSON was expected, or an error page such as a proxy's HTML 502 | `StatusCode`, `BodyExcerpt` |
| `RedirectException` | A 3xx response. Redirects are never followed, so tokens never go elsewhere. | `StatusCode` |
| `LettermintConfigException` | A missing or unrecognised token, an invalid option or ID | |
| `LettermintValidationException` | The SDK rejected the request before sending it, such as invalid tags | `Field` |
| `WebhookVerificationException` | A webhook delivery is not genuine | `Reason`, `ReasonCode` |

The status classes extend `LettermintApiException`. `Code` and `Message` come from the API's error body (`{ "error": { "code", "message", "details" } }` or `{ "message", "errors" }`). The timeout exception is called `LettermintTimeoutException` so that it does not clash with `System.TimeoutException`. `Lettermint.AuthenticationException` has the same simple name as `System.Security.Authentication.AuthenticationException`; qualify it if a file imports both namespaces.

```csharp
try
{
    await lettermint.Emails.SendAsync(message, new IdempotentRequestOptions { IdempotencyKey = key });
}
catch (ValidationException error)
{
    Console.Error.WriteLine($"{error.Message} {string.Join(", ", error.Errors?.Keys ?? Enumerable.Empty<string>())}");
}
catch (RateLimitException error)
{
    await Task.Delay(error.RetryAfter ?? TimeSpan.FromSeconds(1)); // then retry with the same key
}
catch (LettermintTimeoutException)
{
    // The outcome is unknown. Retry with the same idempotency key.
}
catch (LettermintApiException error)
{
    Console.Error.WriteLine($"{(int)error.StatusCode} {error.Code} {error.Message}");
}
```

Exceptions never contain request headers or tokens. `ToString()`, the debugger and JSON serialization of the client, its sub-clients, the options, builders and the webhook verifier show no tokens or secrets; `ToString()` of records that hold a secret returned by the API (such as `ProjectCreatedData.ApiToken`) shows `[redacted]`.

## Webhooks

Verify each webhook delivery before you trust it. Use the webhook's signing secret (`whsec_…`), not an API token, and pass the **raw** request body: the signature covers the exact bytes, so parsing and re-serializing the JSON breaks it.

```csharp
var webhook = new Webhook(Environment.GetEnvironmentVariable("LETTERMINT_WEBHOOK_SECRET")!);

app.MapPost("/webhooks/lettermint", async (HttpRequest request) =>
{
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);
    try
    {
        var delivery = webhook.Verify(buffer.ToArray(), request.Headers);
        // Handle delivery.Event and delivery.Data here.
        return Results.NoContent();
    }
    catch (WebhookVerificationException)
    {
        return Results.BadRequest();
    }
});
```

`Verify(rawBody, headers)` takes the body as a `string` or bytes, and the headers as ASP.NET Core's `IHeaderDictionary`, `HttpHeaders`, a `Dictionary<string, string>` or any other sequence of name/value pairs. It requires `X-Lettermint-Signature` and `X-Lettermint-Delivery` (header names are case-insensitive), checks the HMAC-SHA256 signature in constant time, checks that the delivery timestamp equals the signed one and is within the tolerance, and returns a `WebhookPayload` (`Id`, `Event`, `Timestamp`, `Data` as a `JsonElement`, other fields in `AdditionalProperties`). Otherwise it throws `WebhookVerificationException` with a `Reason` (`ReasonCode` gives the snake_case code all Lettermint SDKs use, such as `signature_mismatch`).

The default tolerance is 300 seconds in either direction: `new Webhook(secret, TimeSpan.FromSeconds(60))`. Zero accepts only the current second; it does not disable the check. A third argument takes a `TimeProvider` for tests. A valid signature does not prevent a repeated delivery within the tolerance, so track `Id` if you must not process an event twice. If the headers are not at hand, call `webhook.VerifySignature(rawBody, signatureHeader, deliveryHeader)`. `payload.GetData<T>()` deserializes `Data`. `Event` is a `WebhookEvent`; unknown event names keep their raw value.

## Types

Request and response types are generated from the Lettermint API specification into the `Lettermint.Models` namespace, for example `SendMailRequest`, `SendMailResponse`, `DomainData` and `ListDomainsResponse`.

- Records with `init` properties. Required fields use the `required` modifier. Fields the SDK does not know are kept in `AdditionalProperties`.
- Optional and nullable are separate. In request types, an optional field that may be `null` is an `Optional<T?>`: leave it unset to omit it, or set it to `null` to send `null` (for example `new UpdateWebhookData { BasicAuth = null }` removes Basic Auth, `new UpdateWebhookData()` leaves it). Values convert implicitly: `Html = "<p>Hi</p>"`. In response types, an absent field and `null` both read as `null`.
- Enums are open: `MessageStatus` is a struct with static values (`MessageStatus.Delivered`), and a value the API adds later decodes as well and keeps its raw string. Compare with `==`, read the raw value with `ToString()`, and check `IsKnown`. Because the values are not constants, use `if`/`switch` on `ToString()` or `when` clauses instead of `case MessageStatus.Delivered:`.
- Lists are `IReadOnlyList<T>` and maps are `IReadOnlyDictionary<string, T>`. Dates are ISO 8601 strings, as the API sends them.

## Runtime support

- .NET 8 and .NET 10 (tested in CI on Linux, Windows and macOS).
- No dependencies outside the .NET runtime.
- The `User-Agent` header is `lettermint-dotnet/<version>`.

## Development

```sh
dotnet test tests/Lettermint.Tests/Lettermint.Tests.csproj
dotnet pack src/Lettermint/Lettermint.csproj -c Release -o artifacts
python3 scripts/generate.py --check
```

Without a local .NET SDK, run the same commands in Docker, for example `docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test tests/Lettermint.Tests/Lettermint.Tests.csproj -f net10.0`.

`src/Lettermint/Generated/` is generated by the private [SDK generator](https://github.com/lettermint/sdk-generator). Do not edit it by hand. With a checkout of the generator, `python3 scripts/generate.py` regenerates the files and `python3 scripts/generate.py --check` verifies them; set `LETTERMINT_SDK_GENERATOR` to the checkout (default `../sdk-generator`). Without the generator, as in CI, `--check` only verifies the generated headers, which record the spec commit and the hashes of both specifications.

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## Support

For help, join the [Lettermint Discord server](https://lettermint.co/r/discord).

## License

The MIT License (MIT). See [LICENSE](LICENSE) for details.
