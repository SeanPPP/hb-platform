using System.Security.Cryptography;
using System.Text;
using Hbpos.Api.Services;

namespace Hbpos.Api.Tests;

public sealed class SquareWebhookVerifierTests
{
    [Fact]
    public void IsValid_ReturnsTrue_WhenSignatureMatchesRawBodyAndNotificationUrl()
    {
        const string signatureKey = "sandbox-webhook-key";
        const string notificationUrl = "https://example.com/api/v1/square/webhooks";
        const string rawBody = "{\"event_id\":\"event-001\",\"type\":\"terminal.checkout.updated\"}";
        var verifier = new SquareWebhookVerifier();
        var signature = CreateSignature(signatureKey, notificationUrl, rawBody);

        var isValid = verifier.IsValid(signatureKey, notificationUrl, rawBody, signature);

        Assert.True(isValid);
    }

    [Fact]
    public void IsValid_ReturnsFalse_WhenRawBodyChanges()
    {
        const string signatureKey = "sandbox-webhook-key";
        const string notificationUrl = "https://example.com/api/v1/square/webhooks";
        const string rawBody = "{\"event_id\":\"event-001\",\"type\":\"terminal.checkout.updated\"}";
        var verifier = new SquareWebhookVerifier();
        var signature = CreateSignature(signatureKey, notificationUrl, rawBody);

        var isValid = verifier.IsValid(
            signatureKey,
            notificationUrl,
            "{\"event_id\":\"event-001\",\"type\":\"terminal.checkout.updated\",\"changed\":true}",
            signature);

        Assert.False(isValid);
    }

    private static string CreateSignature(string signatureKey, string notificationUrl, string rawBody)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signatureKey));
        var payload = Encoding.UTF8.GetBytes(notificationUrl + rawBody);
        return Convert.ToBase64String(hmac.ComputeHash(payload));
    }
}
