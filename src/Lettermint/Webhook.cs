using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lettermint.Internal;
using Lettermint.Models;

namespace Lettermint;

/// <summary>Why a webhook delivery failed verification.</summary>
public enum WebhookVerificationReason
{
    /// <summary><c>signature_header_missing</c>: no <c>X-Lettermint-Signature</c> header.</summary>
    SignatureHeaderMissing,

    /// <summary><c>signature_header_malformed</c>: the signature header cannot be parsed, or there is more than one.</summary>
    SignatureHeaderMalformed,

    /// <summary><c>delivery_header_missing</c>: no <c>X-Lettermint-Delivery</c> header.</summary>
    DeliveryHeaderMissing,

    /// <summary><c>delivery_timestamp_mismatch</c>: the delivery header does not equal the signed timestamp.</summary>
    DeliveryTimestampMismatch,

    /// <summary><c>timestamp_out_of_tolerance</c>: the signed timestamp is too far from the current time.</summary>
    TimestampOutOfTolerance,

    /// <summary><c>signature_mismatch</c>: no <c>v1</c> signature matches.</summary>
    SignatureMismatch,

    /// <summary><c>body_invalid</c>: the raw body is missing or empty.</summary>
    BodyInvalid,

    /// <summary><c>payload_invalid</c>: the signed body is not a JSON object.</summary>
    PayloadInvalid,
}

/// <summary>A webhook delivery could not be verified. Reject the request; do not process its payload.</summary>
public sealed class WebhookVerificationException : LettermintException
{
    /// <summary>Creates the exception.</summary>
    public WebhookVerificationException(WebhookVerificationReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    /// <summary>Why verification failed.</summary>
    public WebhookVerificationReason Reason { get; }

    /// <summary>The reason as the snake_case code all Lettermint SDKs use, for example <c>signature_mismatch</c>.</summary>
    public string ReasonCode => Reason switch
    {
        WebhookVerificationReason.SignatureHeaderMissing => "signature_header_missing",
        WebhookVerificationReason.SignatureHeaderMalformed => "signature_header_malformed",
        WebhookVerificationReason.DeliveryHeaderMissing => "delivery_header_missing",
        WebhookVerificationReason.DeliveryTimestampMismatch => "delivery_timestamp_mismatch",
        WebhookVerificationReason.TimestampOutOfTolerance => "timestamp_out_of_tolerance",
        WebhookVerificationReason.SignatureMismatch => "signature_mismatch",
        WebhookVerificationReason.BodyInvalid => "body_invalid",
        _ => "payload_invalid",
    };
}

/// <summary>A verified webhook delivery. Unknown fields are kept in <see cref="AdditionalProperties"/>.</summary>
public sealed record WebhookPayload
{
    /// <summary>The delivery id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The event name, for example <c>message.delivered</c>. Unknown events keep their raw value.</summary>
    [JsonPropertyName("event")]
    public WebhookEvent Event { get; init; }

    /// <summary>When the event occurred, ISO 8601.</summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }

    /// <summary>The event data.</summary>
    [JsonPropertyName("data")]
    public JsonElement Data { get; init; }

    /// <summary>Other fields of the payload, as raw JSON.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }

    /// <summary>Deserializes <see cref="Data"/> into <typeparamref name="T"/>.</summary>
    public T? GetData<T>() => Data.ValueKind == JsonValueKind.Undefined ? default : Data.Deserialize<T>(LettermintJson.Options);
}

/// <summary>
/// Verifies Lettermint webhook deliveries: an HMAC-SHA256 signature over
/// <c>"&lt;t&gt;." + raw body</c> with the endpoint's signing secret (<c>whsec_…</c>),
/// compared in constant time.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public sealed class Webhook
{
    private const string SignatureHeader = "X-Lettermint-Signature";
    private const string DeliveryHeader = "X-Lettermint-Delivery";

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly byte[] _key;

    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a verifier.</summary>
    /// <param name="secret">The webhook's signing secret, including its <c>whsec_</c> prefix.</param>
    /// <param name="tolerance">
    /// Maximum difference between the signed timestamp and the current time, in either
    /// direction. Default 300 seconds. Zero accepts only the current second.
    /// </param>
    /// <param name="timeProvider">The clock. Default <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="LettermintConfigException">The secret is empty or the tolerance is negative.</exception>
    public Webhook(string secret, TimeSpan? tolerance = null, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrEmpty(secret))
        {
            throw new LettermintConfigException("The webhook signing secret must be a non-empty string.");
        }
        var value = tolerance ?? TimeSpan.FromSeconds(300);
        if (value < TimeSpan.Zero)
        {
            throw new LettermintConfigException("The webhook tolerance must not be negative.");
        }
        _key = Encoding.UTF8.GetBytes(secret);
        Tolerance = TimeSpan.FromSeconds(Math.Floor(value.TotalSeconds));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The timestamp tolerance, in whole seconds.</summary>
    public TimeSpan Tolerance { get; }

    /// <summary>
    /// Verifies a delivery from its raw body and request headers, and returns the
    /// decoded payload. Requires <c>X-Lettermint-Signature</c> and <c>X-Lettermint-Delivery</c>
    /// (which must equal the signed timestamp). Header names are case-insensitive.
    /// </summary>
    /// <param name="rawBody">The request body exactly as received.</param>
    /// <param name="headers">
    /// The request headers: <see cref="System.Net.Http.Headers.HttpHeaders"/>, ASP.NET Core's
    /// <c>IHeaderDictionary</c>, a <c>Dictionary&lt;string, string&gt;</c> or any other sequence of
    /// name/value pairs whose value is a string or a list of strings.
    /// </param>
    /// <exception cref="WebhookVerificationException">The delivery is not genuine.</exception>
    public WebhookPayload Verify<TValue>(string rawBody, IEnumerable<KeyValuePair<string, TValue>> headers)
    {
        var (signature, delivery) = ReadHeaders(headers);
        return VerifySignature(rawBody, signature, delivery);
    }

    /// <inheritdoc cref="Verify{TValue}(string, IEnumerable{KeyValuePair{string, TValue}})"/>
    public WebhookPayload Verify<TValue>(ReadOnlySpan<byte> rawBody, IEnumerable<KeyValuePair<string, TValue>> headers)
    {
        var (signature, delivery) = ReadHeaders(headers);
        return VerifySignature(rawBody, signature, delivery);
    }

    /// <summary>
    /// Verifies the raw body against an <c>X-Lettermint-Signature</c> value, for setups
    /// where the headers are not at hand. When <paramref name="timestamp"/> (the
    /// <c>X-Lettermint-Delivery</c> value) is given, it must equal the signed timestamp.
    /// </summary>
    /// <exception cref="WebhookVerificationException">The delivery is not genuine.</exception>
    public WebhookPayload VerifySignature(string rawBody, string? signatureHeader, string? timestamp = null)
    {
        if (rawBody is null)
        {
            throw new WebhookVerificationException(WebhookVerificationReason.BodyInvalid, "Pass the raw request body.");
        }
        return VerifySignature(Encoding.UTF8.GetBytes(rawBody), signatureHeader, timestamp);
    }

    /// <inheritdoc cref="VerifySignature(string, string?, string?)"/>
    public WebhookPayload VerifySignature(ReadOnlySpan<byte> rawBody, string? signatureHeader, string? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            throw new WebhookVerificationException(WebhookVerificationReason.SignatureHeaderMissing, "The X-Lettermint-Signature header is missing.");
        }
        var (signedAt, signatures) = ParseSignatureHeader(signatureHeader);
        if (timestamp is not null && timestamp.Trim() != signedAt)
        {
            throw new WebhookVerificationException(WebhookVerificationReason.DeliveryTimestampMismatch, "The X-Lettermint-Delivery header does not match the signed timestamp.");
        }
        if (rawBody.IsEmpty)
        {
            throw new WebhookVerificationException(WebhookVerificationReason.BodyInvalid, "The raw request body is empty.");
        }
        var seconds = long.Parse(signedAt, NumberStyles.None, CultureInfo.InvariantCulture);
        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (Math.Abs((decimal)now - seconds) > (decimal)Tolerance.TotalSeconds)
        {
            throw new WebhookVerificationException(WebhookVerificationReason.TimestampOutOfTolerance, "The signed timestamp is outside the allowed tolerance.");
        }

        var prefix = Encoding.ASCII.GetBytes(signedAt + ".");
        var signed = new byte[prefix.Length + rawBody.Length];
        prefix.CopyTo(signed, 0);
        rawBody.CopyTo(signed.AsSpan(prefix.Length));
        var expected = HMACSHA256.HashData(_key, signed);
        var matched = false;
        foreach (var candidate in signatures)
        {
            matched |= CryptographicOperations.FixedTimeEquals(candidate, expected);
        }
        if (!matched)
        {
            throw new WebhookVerificationException(WebhookVerificationReason.SignatureMismatch, "The webhook signature does not match.");
        }

        try
        {
            using var document = JsonDocument.Parse(signed.AsMemory(prefix.Length));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new WebhookVerificationException(WebhookVerificationReason.PayloadInvalid, "The webhook payload is not a JSON object.");
            }
            return document.RootElement.Deserialize<WebhookPayload>(LettermintJson.Options)
                ?? throw new WebhookVerificationException(WebhookVerificationReason.PayloadInvalid, "The webhook payload is not a JSON object.");
        }
        catch (JsonException)
        {
            throw new WebhookVerificationException(WebhookVerificationReason.PayloadInvalid, "The webhook payload is not valid JSON.");
        }
    }

    /// <summary>Shows the tolerance; never the secret.</summary>
    public override string ToString() => $"Webhook {{ Tolerance = {Tolerance.TotalSeconds:0}s }}";

    private static (string Signature, string Delivery) ReadHeaders<TValue>(IEnumerable<KeyValuePair<string, TValue>> headers)
    {
        var signatures = new List<string>();
        var deliveries = new List<string>();
        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                var target = string.Equals(name, SignatureHeader, StringComparison.OrdinalIgnoreCase) ? signatures
                    : string.Equals(name, DeliveryHeader, StringComparison.OrdinalIgnoreCase) ? deliveries
                    : null;
                if (target is null || value is null)
                {
                    continue;
                }
                switch (value)
                {
                    case string text:
                        target.Add(text);
                        break;
                    case IEnumerable values:
                        foreach (var item in values)
                        {
                            if (item is not null)
                            {
                                target.Add(item.ToString() ?? string.Empty);
                            }
                        }
                        break;
                    default:
                        target.Add(value.ToString() ?? string.Empty);
                        break;
                }
            }
        }
        if (signatures.Count == 0)
        {
            throw new WebhookVerificationException(WebhookVerificationReason.SignatureHeaderMissing, "The X-Lettermint-Signature header is missing.");
        }
        if (signatures.Count > 1)
        {
            throw new WebhookVerificationException(WebhookVerificationReason.SignatureHeaderMalformed, "The request has more than one X-Lettermint-Signature header.");
        }
        if (deliveries.Count == 0)
        {
            throw new WebhookVerificationException(WebhookVerificationReason.DeliveryHeaderMissing, "The X-Lettermint-Delivery header is missing.");
        }
        if (deliveries.Count > 1)
        {
            throw new WebhookVerificationException(WebhookVerificationReason.DeliveryTimestampMismatch, "The request has more than one X-Lettermint-Delivery header.");
        }
        return (signatures[0], deliveries[0]);
    }

    private static (string Timestamp, List<byte[]> Signatures) ParseSignatureHeader(string header)
    {
        static WebhookVerificationException Malformed(string detail) =>
            new(WebhookVerificationReason.SignatureHeaderMalformed, $"The signature header is malformed: {detail}.");

        foreach (var character in header)
        {
            if (character < '\x20' || character > '\x7e')
            {
                throw Malformed("it contains non-ASCII or control characters");
            }
        }
        string? timestamp = null;
        var signatures = new List<byte[]>();
        foreach (var part in header.Split(','))
        {
            var entry = part.Trim();
            var separator = entry.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }
            var key = entry[..separator];
            var value = entry[(separator + 1)..];
            if (key == "t")
            {
                if (timestamp is not null)
                {
                    throw Malformed("it has more than one timestamp");
                }
                if (value.Length == 0 || value.Length > 15 || !value.All(char.IsAsciiDigit))
                {
                    throw Malformed("the timestamp is not a number of seconds");
                }
                timestamp = value;
            }
            else if (key == "v1" && value.Length == 64 && value.All(char.IsAsciiHexDigit))
            {
                signatures.Add(Convert.FromHexString(value));
            }
        }
        if (timestamp is null)
        {
            throw Malformed("the timestamp (t=) is missing");
        }
        if (signatures.Count == 0)
        {
            throw Malformed("no v1 signature is present");
        }
        return (timestamp, signatures);
    }
}
