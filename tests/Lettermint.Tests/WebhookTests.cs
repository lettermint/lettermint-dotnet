using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Lettermint.Tests;

public class WebhookTests
{
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(1700000000);
    }
    private static string Sign(string body, long timestamp = 1700000000)
        => $"t={timestamp},v1={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("test-secret"), Encoding.UTF8.GetBytes(timestamp + "." + body))).ToLowerInvariant()}";

    [Fact]
    public void VerifiesRawPayloadAndMultipleSignatures()
    {
        const string body = "{\"event\":\"message.sent\"}";
        var result = Webhook.Verify(body, Sign(body) + ",v1=" + new string('0', 64), "test-secret", timeProvider: new Clock());
        Assert.Equal("message.sent", result.GetProperty("event").GetString());
        Assert.Throws<WebhookVerificationException>(() => Webhook.Verify(body + " ", Sign(body), "test-secret", timeProvider: new Clock()));
        Assert.Throws<WebhookVerificationException>(() => Webhook.Verify(body, Sign(body), "wrong", timeProvider: new Clock()));
    }

    [Theory]
    [InlineData(1699999699)]
    [InlineData(1700000001)]
    public void RejectsOldAndFutureTimestamps(long timestamp)
    {
        Assert.Throws<WebhookVerificationException>(() => Webhook.Verify("{}", Sign("{}", timestamp), "test-secret", timeProvider: new Clock()));
    }

    [Theory]
    [InlineData("v1=xyz")]
    [InlineData("t=1700000000,v1=xyz")]
    [InlineData("t=1700000000,t=1700000000,v1=xyz")]
    public void RejectsMalformedSignatures(string signature)
    {
        Assert.Throws<WebhookVerificationException>(() => Webhook.Verify("{}", signature, "test-secret", timeProvider: new Clock()));
    }

    [Fact]
    public void RejectsSignedInvalidJsonAndAllowsExplicitAgeCheckOptOut()
    {
        Assert.Throws<WebhookVerificationException>(() => Webhook.Verify("invalid", Sign("invalid"), "test-secret", timeProvider: new Clock()));
        Webhook.Verify("{}", Sign("{}", 1), "test-secret", toleranceSeconds: 0, timeProvider: new Clock());
    }
}
