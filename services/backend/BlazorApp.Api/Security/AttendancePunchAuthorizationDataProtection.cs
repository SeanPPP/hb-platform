using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorApp.Shared.Security;
using Microsoft.AspNetCore.DataProtection;

namespace BlazorApp.Api.Security;

public static class AttendancePunchAuthorizationDataProtection
{
    public static AttendancePunchAuthorizationProtector CreateProtector(
        IDataProtectionProvider provider) =>
        new(provider.CreateProtector(
            BlazorApp.Shared.Security.AttendanceQrKeyDataProtection.PunchAuthorizationPurpose));
}

public readonly record struct AttendancePunchAuthorization(string Token, DateTime ExpiresAtUtc);

public enum AttendancePunchAuthorizationValidationResult
{
    Valid,
    Invalid,
    Expired,
}

public sealed class AttendancePunchAuthorizationProtector(IDataProtector protector)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private const int PayloadVersion = 1;
    private const int MaxProtectedTokenLength = 4096;

    public AttendancePunchAuthorization Issue(
        string userGuid,
        string qrToken,
        string signingKeyId,
        AttendanceQrTokenPayload qrPayload,
        DateTime issuedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userGuid);
        ArgumentException.ThrowIfNullOrWhiteSpace(qrToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKeyId);
        ArgumentNullException.ThrowIfNull(qrPayload);

        var issuedAt = NormalizeUtc(issuedAtUtc);
        var expiresAt = issuedAt.Add(Lifetime);
        var payload = new ProtectedPayload
        {
            Version = PayloadVersion,
            UserGuid = userGuid,
            QrSha256 = ComputeQrSha256(qrToken),
            TokenId = qrPayload.TokenId,
            SigningKeyId = signingKeyId,
            StoreCode = qrPayload.StoreCode,
            DeviceCode = qrPayload.DeviceCode,
            IssuedAtUtc = issuedAt,
            ExpiresAtUtc = expiresAt,
        };

        // 关键逻辑：凭证只保存二维码摘要和服务端验证后的身份，不保存原二维码或签码密钥。
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload);
        try
        {
            return new AttendancePunchAuthorization(
                Convert.ToBase64String(protector.Protect(plaintext)),
                expiresAt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public AttendancePunchAuthorizationValidationResult Validate(
        string? authorizationToken,
        string userGuid,
        string qrToken,
        string signingKeyId,
        AttendanceQrTokenPayload qrPayload,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(authorizationToken)
            || authorizationToken.Length > MaxProtectedTokenLength)
        {
            return AttendancePunchAuthorizationValidationResult.Invalid;
        }

        byte[]? plaintext = null;
        try
        {
            plaintext = protector.Unprotect(Convert.FromBase64String(authorizationToken));
            var payload = JsonSerializer.Deserialize<ProtectedPayload>(plaintext);
            if (payload == null || payload.Version != PayloadVersion)
            {
                return AttendancePunchAuthorizationValidationResult.Invalid;
            }

            var issuedAt = NormalizeUtc(payload.IssuedAtUtc);
            var expiresAt = NormalizeUtc(payload.ExpiresAtUtc);
            var now = NormalizeUtc(utcNow);
            if (expiresAt != issuedAt.Add(Lifetime) || now < issuedAt)
            {
                return AttendancePunchAuthorizationValidationResult.Invalid;
            }
            if (now >= expiresAt)
            {
                return AttendancePunchAuthorizationValidationResult.Expired;
            }

            var expectedQrHash = ComputeQrSha256(qrToken);
            var hashMatches = payload.QrSha256 is { Length: 32 }
                && CryptographicOperations.FixedTimeEquals(payload.QrSha256, expectedQrHash);
            CryptographicOperations.ZeroMemory(expectedQrHash);

            return hashMatches
                && string.Equals(payload.UserGuid, userGuid, StringComparison.Ordinal)
                && payload.TokenId == qrPayload.TokenId
                && string.Equals(payload.SigningKeyId, signingKeyId, StringComparison.Ordinal)
                && string.Equals(payload.StoreCode, qrPayload.StoreCode, StringComparison.Ordinal)
                && string.Equals(payload.DeviceCode, qrPayload.DeviceCode, StringComparison.Ordinal)
                    ? AttendancePunchAuthorizationValidationResult.Valid
                    : AttendancePunchAuthorizationValidationResult.Invalid;
        }
        catch (Exception exception) when (exception is CryptographicException
            or FormatException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            return AttendancePunchAuthorizationValidationResult.Invalid;
        }
        finally
        {
            if (plaintext != null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    private static byte[] ComputeQrSha256(string qrToken)
    {
        var qrBytes = Encoding.UTF8.GetBytes(qrToken);
        try
        {
            return SHA256.HashData(qrBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(qrBytes);
        }
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private sealed class ProtectedPayload
    {
        public int Version { get; set; }
        public string UserGuid { get; set; } = string.Empty;
        public byte[] QrSha256 { get; set; } = [];
        public Guid TokenId { get; set; }
        public string SigningKeyId { get; set; } = string.Empty;
        public string StoreCode { get; set; } = string.Empty;
        public string DeviceCode { get; set; } = string.Empty;
        public DateTime IssuedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}
