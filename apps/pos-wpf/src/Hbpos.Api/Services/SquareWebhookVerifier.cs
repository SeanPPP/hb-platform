using System.Security.Cryptography;
using System.Text;

namespace Hbpos.Api.Services;

public interface ISquareWebhookVerifier
{
    bool IsValid(
        string signatureKey,
        string notificationUrl,
        string rawBody,
        string? signatureHeader);
}

public sealed class SquareWebhookVerifier : ISquareWebhookVerifier
{
    public bool IsValid(
        string signatureKey,
        string notificationUrl,
        string rawBody,
        string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureKey) ||
            string.IsNullOrWhiteSpace(notificationUrl) ||
            rawBody is null ||
            string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signatureKey));
        // Square 验签必须拼接原始 notification URL 和原始 body，不能对 JSON 做重排或格式化。
        var payload = Encoding.UTF8.GetBytes(notificationUrl + rawBody);
        var expectedSignature = Convert.ToBase64String(hmac.ComputeHash(payload));
        var providedBytes = Encoding.UTF8.GetBytes(signatureHeader.Trim());
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);
        return providedBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
