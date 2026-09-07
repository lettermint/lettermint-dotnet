# Lettermint .NET SDK

[![.NET Version](https://img.shields.io/badge/.NET-8%2B-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](LICENSE)
[![Join our Discord server](https://img.shields.io/discord/1305510095588819035?logo=discord&logoColor=eee&label=Discord&labelColor=464ce5&color=0D0E28&cacheSeconds=43200)](https://lettermint.co/r/discord)

The official .NET SDK for the [Lettermint](https://lettermint.co) sending and team APIs.

## Requirements

- .NET 8 or later. The package targets .NET 8 and .NET 10.

## Installation

This repository has not been published to NuGet. Add a project reference:

```sh
dotnet add path/to/YourApp.csproj reference path/to/lettermint-dotnet/src/Lettermint/Lettermint.csproj
```

## Usage

### Sending Emails

Use a project sending token with `LettermintClient.Email(...)`.

```csharp
using Lettermint;
using Lettermint.Models;

using var email = LettermintClient.Email(
    Environment.GetEnvironmentVariable("LETTERMINT_SENDING_TOKEN")!);

var result = await email.From("Sender <sender@example.com>")
    .To("recipient@example.com")
    .Subject("Hello")
    .Html("<p>Hello from Lettermint.</p>")
    .Text("Hello from Lettermint.")
    .IdempotencyKey("welcome-123")
    .SendAsync();

Console.WriteLine(result.MessageId);
```

Each `From(...)` or `Compose()` call creates a separate builder. Do not share a
builder between concurrent sends. A builder retains its values after a send.
Use a new builder for each new email.

The builder also supports `Cc`, `Bcc`, `ReplyTo`, `Route`, `Tag`, `Headers`,
`Metadata`, `Settings`, and `Attach`. Attachment content must use Base64.
The optional attachment arguments are `contentId` and `contentType`.
`Headers` sets email headers, not HTTP headers.

### Typed Payloads and Batch Sending

```csharp
var payload = new SendMailRequest
{
    From = "sender@example.com",
    To = ["recipient@example.com"],
    Subject = "Hello",
    Text = "Hello from Lettermint."
};
await email.SendAsync(payload);
await email.SendBatchAsync(new SendMailRequest[] { payload },
    new RequestOptions { IdempotencyKey = "batch-123" });
```

Both batch overloads use objects for `headers` and `metadata`, as required by
the API request validators.

### Scheduling, Tags, and TLS

```csharp
var scheduled = await email.From("sender@example.com")
    .To("recipient@example.com")
    .Subject("Scheduled email")
    .Text("Hello from Lettermint.")
    .ScheduledAt("tomorrow at 9am")
    .Tags(new SendMailRequestTagsItem { Name = "campaign", Value = "welcome" })
    .Settings(new SendMailRequestSettings { Tls = TlsPolicy.Enforced })
    .SendAsync();

await email.RescheduleAsync(scheduled.MessageId!,
    new RescheduleMessageRequest { ScheduledAt = "tomorrow at 10am" });
await email.CancelAsync(scheduled.MessageId!);
```

Scheduling uses the API `scheduled_at` field. Send responses expose
`ScheduledAt` and the `Scheduled` status. Cancellation is final.
Full API clients have `Messages.RescheduleAsync` and `Messages.CancelAsync`.
Use `Messages.ProcessAsync` to process a quarantined inbound message.

## Team API

Use a team API token with `LettermintClient.Api(...)`.

```csharp
using var api = LettermintClient.Api(
    Environment.GetEnvironmentVariable("LETTERMINT_API_TOKEN")!);

var page = await api.Domains.ListAsync(new RequestOptions
{
    Query = new Dictionary<string, string> { ["page[size]"] = "10" }
});
foreach (var domain in page.Data ?? [])
    Console.WriteLine(domain.Domain);

Console.WriteLine(await api.PingAsync()); // pong
Console.WriteLine(await email.PingAsync()); // pong
```

Endpoint groups are `Domains`, `Messages`, `Projects`, `Routes`, `Stats`,
`Suppressions`, `Team`, and `Webhooks`. Methods use `ListAsync`, `CreateAsync`,
`RetrieveAsync`, `UpdateAsync`, and `DeleteAsync` where applicable.
`Routes.ListAsync(projectId)` and `Routes.CreateAsync(projectId, payload)`
access the routes of a project. `BlockedFileTypesAsync` is on the full API client.
See `specs/operations.json` for the complete method list.

Sending clients use `x-lettermint-token`. Full API clients use
`Authorization: Bearer`. Do not exchange these tokens.

## Responses and JSON

Responses have generated C# models. Nested objects and lists are typed.
Use `model.ToJson()` to get a `JsonElement`. Unknown object fields are retained
in `AdditionalProperties`. Unknown enum values produce a response parsing error.

List responses expose `Data` and flat cursor fields such as `NextCursor` and
`NextPageUrl`. Route statistics are a typed list. Dictionary properties accept
an object or the empty array that PHP emits for an empty map. Nonempty arrays
are not accepted as maps.

Suppression removal can return HTTP 200 for removal or HTTP 202 for manual
review. Check `Status`; review responses also expose `TicketIdentifier` and
`Confidence`. HTTP 202 does not mean that the suppression was removed.

Optional model properties use null to omit a field. To send an explicit JSON
null, use the `JsonElement` payload overload. This also permits fields that are
not yet in the specification. API validation remains on the server.

`Messages.SourceAsync`, `HtmlAsync`, and `TextAsync` preserve the raw response.
Both ping methods trim whitespace and return `pong`.

## Configuration

Use `ClientOptions` to set `BaseUrl`, `Timeout`, or an existing `HttpClient`.
Keep clients for repeated requests. Dispose them when no longer needed.
An injected `HttpClient` remains owned by the caller. It must not have default
authentication headers. Configure its handler to disable automatic redirects.
The default handler disables redirects to prevent token forwarding.

Every API method accepts a `CancellationToken`. `RequestOptions` supports query
parameters, additional HTTP headers, and an idempotency key. Additional headers
cannot override authentication. The SDK does not retry requests automatically.

## Error Handling

`LettermintApiException` exposes `StatusCode` and the token-redacted
`ResponseBody`. HTTP, timeout, and parsing failures use `LettermintException`.
Caller cancellation uses `OperationCanceledException`.

## Webhook Verification

Use `Webhook.Verify(rawBody, signatureHeader, signingSecret)` to verify a
webhook and get its JSON payload. Pass the original body without changes.
The default timestamp tolerance is 300 seconds. A value of zero disables the
timestamp check. Invalid signatures use `WebhookVerificationException`.

## Testing

Install the .NET 10 SDK and run:

```sh
dotnet test tests/Lettermint.Tests/Lettermint.Tests.csproj
```

## Development

<details>
<summary>Code generation and API contract checks</summary>


Install the .NET 10 SDK and Python 3.10 or later.

```sh
python3 tools/generate.py
python3 tools/generate.py --check
python3 -m unittest discover -s tools -p 'test_*.py'
dotnet test tests/Lettermint.Tests/Lettermint.Tests.csproj
dotnet pack src/Lettermint/Lettermint.csproj -c Release -o artifacts
```

The two specification files in `specs` are independent inputs. To update them,
copy the current sending and full API specifications into that directory and
regenerate. The generator fails on unsupported schema unions.

### API Contract Verification

The SDK was checked against API source revision
`4caab44611b345b1457093a51abbddc5f4be2ccb` on 2026-09-07. The checks cover
52 public API-key routes. OAuth-only identity, connection, and listener routes
are outside this SDK's scope. No live API requests were made.

The tests use synthetic fixtures exported through the actual Laravel data
serializers, paginator, sending response methods, and webhook signature code.
The fixture exporter does not write to the database or send email.

The generator applies three corrections verified against the API code:

- Sending headers and metadata use string-valued maps.
- Route attachment delivery uses a string enum.
- Message and event lists use flat Laravel cursor pages.

It also combines all successful response variants, including HTTP 202 review
responses. The raw specification snapshots remain unchanged for comparison.
Source fingerprints are stored in `specs/api-source-verification.json`.

To check for source changes:

```sh
python3 tools/verify_api_source.py /path/to/api-repository
```

To reproduce the fixtures, use the API repository with its installed PHP
dependencies. Write to a temporary file first and check that it contains JSON:

```sh
php tools/export_api_fixtures.php /path/to/api-repository/laravel > /tmp/api-source.json
python3 -m json.tool /tmp/api-source.json > /dev/null
```

A changed source fingerprint requires another contract review. Do not update
the fingerprint record only to make a failed check pass.


</details>

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for release changes.

## Support

For help, join the [Lettermint Discord server](https://lettermint.co/r/discord).

## Credits

- [Bjarn Bronsveld](https://github.com/bjarn)

## License

The MIT License (MIT). See [LICENSE](LICENSE) for details.
