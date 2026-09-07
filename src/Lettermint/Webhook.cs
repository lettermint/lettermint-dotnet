using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lettermint;

public sealed class WebhookVerificationException(string message) : LettermintException(message);

public static class Webhook
{
    public static JsonElement Verify(string payload, string signature, string secret,
        int toleranceSeconds = 300, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrEmpty(payload) || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(secret))
            throw new WebhookVerificationException("Payload, signature, and secret are required.");
        ArgumentOutOfRangeException.ThrowIfNegative(toleranceSeconds);
        string? timestamp = null;
        var hashes = new List<byte[]>();
        foreach (var part in signature.Split(','))
        {
            var pair = part.Trim().Split('=', 2);
            if (pair.Length != 2) continue;
            if (pair[0] == "t")
            {
                if (timestamp != null) throw new WebhookVerificationException("Duplicate timestamp.");
                timestamp = pair[1];
            }
            if (pair[0] == "v1" && pair[1].Length == 64)
            {
                try { hashes.Add(Convert.FromHexString(pair[1])); }
                catch (FormatException) { /* A malformed signature cannot match. */ }
            }
        }
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || hashes.Count == 0)
            throw new WebhookVerificationException("Invalid signature format.");
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().ToUnixTimeSeconds();
        if (toleranceSeconds > 0 && (seconds > now || seconds < now - toleranceSeconds))
            throw new WebhookVerificationException("The signature timestamp is outside the allowed range.");
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(timestamp + "." + payload));
        if (!hashes.Any(hash => CryptographicOperations.FixedTimeEquals(hash, expected)))
            throw new WebhookVerificationException("The webhook signature does not match.");
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }
        catch (JsonException) { throw new WebhookVerificationException("The webhook payload is not valid JSON."); }
    }
}
