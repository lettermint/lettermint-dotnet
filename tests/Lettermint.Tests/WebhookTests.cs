using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Lettermint.Models;

namespace Lettermint.Tests;

public class WebhookTests
{
    private const string Secret = "whsec_test0123456789abcdefABCDEF01";
    private const long Now = 1767225600;
    private const string Body = """{"id":"01999b0e","event":"message.delivered","timestamp":"2025-12-31T23:59:30.000000Z","data":{"message_id":"m1","subject":"Hé"},"context":{"project_id":"p"}}""";

    private sealed class FixedClock(long seconds) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    private static Webhook Verifier(TimeSpan? tolerance = null, string secret = Secret) => new(secret, tolerance, new FixedClock(Now));

    private static string Sign(string body, long timestamp, string secret = Secret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();

    private static Dictionary<string, string> Headers(long timestamp, string? signature = null, string body = Body) => new()
    {
        ["X-Lettermint-Signature"] = $"t={timestamp},v1={signature ?? Sign(body, timestamp)}",
        ["X-Lettermint-Delivery"] = timestamp.ToString(),
    };

    private static WebhookVerificationReason Fails(Action verify) => Assert.Throws<WebhookVerificationException>(verify).Reason;

    [Fact]
    public void VerifiesADeliveryAndReturnsThePayload()
    {
        var payload = Verifier().Verify(Body, Headers(Now - 30));
        Assert.Equal(WebhookEvent.MessageDelivered, payload.Event);
        Assert.Equal("01999b0e", payload.Id);
        Assert.Equal("2025-12-31T23:59:30.000000Z", payload.Timestamp);
        Assert.Equal("Hé", payload.Data.GetProperty("subject").GetString());
        Assert.Equal("p", payload.AdditionalProperties!["context"].GetProperty("project_id").GetString());
        Assert.Equal("m1", payload.GetData<Dictionary<string, string>>()!["message_id"]);
    }

    [Fact]
    public void AcceptsCommonHeaderTypesCaseInsensitively()
    {
        var headers = Headers(Now);
        Verifier().Verify(Body, headers.ToDictionary(p => p.Key.ToLowerInvariant(), p => p.Value));
        Verifier().Verify(Body, headers.ToDictionary(p => p.Key.ToUpperInvariant(), p => new[] { p.Value }));
        using var request = new HttpRequestMessage();
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
        Verifier().Verify(Body, request.Headers);
        Verifier().Verify(Encoding.UTF8.GetBytes(Body), headers);
        Verifier().Verify(Body, new List<KeyValuePair<string, IEnumerable<string>>> { new("x-lettermint-signature", [headers["X-Lettermint-Signature"]]), new("x-lettermint-delivery", [headers["X-Lettermint-Delivery"]]) });
    }

    [Fact]
    public void RequiresBothHeaders()
    {
        var headers = Headers(Now);
        Assert.Equal(WebhookVerificationReason.SignatureHeaderMissing, Fails(() => Verifier().Verify(Body, new Dictionary<string, string> { ["X-Lettermint-Delivery"] = Now.ToString() })));
        Assert.Equal(WebhookVerificationReason.DeliveryHeaderMissing, Fails(() => Verifier().Verify(Body, new Dictionary<string, string> { ["X-Lettermint-Signature"] = headers["X-Lettermint-Signature"] })));
        Assert.Equal(WebhookVerificationReason.DeliveryTimestampMismatch, Fails(() => Verifier().Verify(Body, new Dictionary<string, string> { ["X-Lettermint-Signature"] = headers["X-Lettermint-Signature"], ["X-Lettermint-Delivery"] = (Now + 1).ToString() })));
        Assert.Equal(WebhookVerificationReason.SignatureHeaderMalformed, Fails(() => Verifier().Verify(Body, new Dictionary<string, string[]> { ["X-Lettermint-Signature"] = [headers["X-Lettermint-Signature"], headers["X-Lettermint-Signature"]], ["X-Lettermint-Delivery"] = [Now.ToString()] })));
        Assert.Equal(WebhookVerificationReason.DeliveryTimestampMismatch, Fails(() => Verifier().Verify(Body, new Dictionary<string, string[]> { ["X-Lettermint-Signature"] = [headers["X-Lettermint-Signature"]], ["X-Lettermint-Delivery"] = [Now.ToString(), Now.ToString()] })));
        Assert.Equal(WebhookVerificationReason.SignatureHeaderMissing, Fails(() => Verifier().Verify(Body, (IEnumerable<KeyValuePair<string, string>>)null!)));
    }

    [Fact]
    public void AcceptsAnyMatchingV1()
    {
        var good = Sign(Body, Now);
        var bad = new string('a', 64);
        Verifier().VerifySignature(Body, $"t={Now},v1={bad},v1={good}", Now.ToString());
        Verifier().VerifySignature(Body, $"t={Now},v1={good},v1={bad}");
        Verifier().VerifySignature(Body, $"t={Now},v0=abc,v1={good}");
        Assert.Equal(WebhookVerificationReason.SignatureMismatch, Fails(() => Verifier().VerifySignature(Body, $"t={Now},v1={bad},v1={bad}")));
    }

    [Theory]
    [InlineData(300, true)]
    [InlineData(-300, true)]
    [InlineData(301, false)]
    [InlineData(-301, false)]
    [InlineData(0, true)]
    public void ChecksTheToleranceInBothDirections(long offset, bool valid)
    {
        var timestamp = Now + offset;
        var verify = () => Verifier().Verify(Body, Headers(timestamp));
        if (valid)
        {
            verify();
        }
        else
        {
            Assert.Equal(WebhookVerificationReason.TimestampOutOfTolerance, Fails(() => verify()));
        }
    }

    [Fact]
    public void ToleranceZeroAcceptsOnlyTheCurrentSecond()
    {
        Verifier(TimeSpan.Zero).Verify(Body, Headers(Now));
        Assert.Equal(WebhookVerificationReason.TimestampOutOfTolerance, Fails(() => Verifier(TimeSpan.Zero).Verify(Body, Headers(Now - 1))));
        Assert.Equal(TimeSpan.FromSeconds(60), Verifier(TimeSpan.FromSeconds(60.9)).Tolerance);
    }

    [Theory]
    [InlineData("t=1767225600")]
    [InlineData("v1=0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("t=abc,v1=0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("t=1767225600,t=1767225600,v1=0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("t=1767225600,v1=00000000000000000000000000000000000000000000000000000000000000é0")]
    [InlineData("t=１７６７２２５６００,v1=0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("t=-1,v1=0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("garbage")]
    public void MalformedSignatureHeadersFailWithoutCrashing(string header)
    {
        Assert.Equal(WebhookVerificationReason.SignatureHeaderMalformed, Fails(() => Verifier().VerifySignature(Body, header)));
    }

    [Fact]
    public void SignsTheExactBytesWithTheSecretAsGiven()
    {
        var headers = Headers(Now);
        Assert.Equal(WebhookVerificationReason.SignatureMismatch, Fails(() => Verifier().Verify(Body.Replace("Hé", "He"), headers)));
        Assert.Equal(WebhookVerificationReason.SignatureMismatch, Fails(() => Verifier().Verify(Body.Replace(",", ", "), headers)));
        Assert.Equal(WebhookVerificationReason.SignatureMismatch, Fails(() => Verifier(secret: Secret["whsec_".Length..]).Verify(Body, headers)));
        Assert.Equal(WebhookVerificationReason.SignatureMismatch, Fails(() => Verifier(secret: "whsec_other").Verify(Body, headers)));
    }

    [Fact]
    public void RejectsEmptyBodiesAndPayloadsThatAreNotObjects()
    {
        Assert.Equal(WebhookVerificationReason.BodyInvalid, Fails(() => Verifier().Verify("", Headers(Now, body: ""))));
        Assert.Equal(WebhookVerificationReason.BodyInvalid, Fails(() => Verifier().VerifySignature((string)null!, "t=1,v1=" + new string('0', 64))));
        Assert.Equal(WebhookVerificationReason.PayloadInvalid, Fails(() => Verifier().Verify("[1]", Headers(Now, body: "[1]"))));
        Assert.Equal(WebhookVerificationReason.PayloadInvalid, Fails(() => Verifier().Verify("not json", Headers(Now, body: "not json"))));
    }

    [Fact]
    public void ValidatesTheConfiguration()
    {
        Assert.Throws<LettermintConfigException>(() => new Webhook(""));
        Assert.Throws<LettermintConfigException>(() => new Webhook(null!));
        Assert.Throws<LettermintConfigException>(() => new Webhook(Secret, TimeSpan.FromSeconds(-1)));
        Assert.Equal(TimeSpan.FromSeconds(300), new Webhook(Secret).Tolerance);
    }

    [Fact]
    public void ReasonCodesMatchTheOtherSdks()
    {
        var codes = Enum.GetValues<WebhookVerificationReason>().Select(reason => new WebhookVerificationException(reason, "x").ReasonCode);
        Assert.Equal(["signature_header_missing", "signature_header_malformed", "delivery_header_missing", "delivery_timestamp_mismatch", "timestamp_out_of_tolerance", "signature_mismatch", "body_invalid", "payload_invalid"], codes);
    }

    [Fact]
    public void NeverShowsTheSecret()
    {
        var webhook = Verifier();
        Assert.Equal("Webhook { Tolerance = 300s }", webhook.ToString());
        var error = Assert.Throws<WebhookVerificationException>(() => webhook.Verify(Body, Headers(Now, new string('0', 64))));
        Assert.DoesNotContain(Secret, error.ToString());
        Assert.DoesNotContain(Secret, System.Text.Json.JsonSerializer.Serialize(webhook));
    }

    [Fact]
    public void UnknownEventsKeepTheirRawValue()
    {
        var body = """{"event":"message.something_new","data":{}}""";
        var payload = Verifier().Verify(body, Headers(Now, body: body));
        Assert.Equal("message.something_new", payload.Event.ToString());
        Assert.False(payload.Event.IsKnown);
    }
}
