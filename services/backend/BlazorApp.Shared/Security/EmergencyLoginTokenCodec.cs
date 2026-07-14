using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace BlazorApp.Shared.Security;

public sealed class EmergencyLoginTokenPayload
{
    [JsonPropertyOrder(0)]
    [JsonPropertyName("grantId")]
    public Guid GrantId { get; set; }

    [JsonPropertyOrder(1)]
    [JsonPropertyName("storeCode")]
    public string StoreCode { get; set; } = string.Empty;

    [JsonPropertyOrder(2)]
    [JsonPropertyName("businessDate")]
    public string BusinessDate { get; set; } = string.Empty;

    [JsonPropertyOrder(3)]
    [JsonPropertyName("permissionProfile")]
    public string PermissionProfile { get; set; } = EmergencyLoginTokenCodec.AllPosTerminalProfile;

    [JsonPropertyOrder(4)]
    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    [JsonPropertyOrder(5)]
    [JsonPropertyName("audience")]
    public string Audience { get; set; } = EmergencyLoginTokenCodec.WpfAudience;

    [JsonPropertyOrder(6)]
    [JsonPropertyName("issuedAtUtc")]
    public DateTime IssuedAtUtc { get; set; }

    [JsonPropertyOrder(7)]
    [JsonPropertyName("notBeforeUtc")]
    public DateTime NotBeforeUtc { get; set; }

    [JsonPropertyOrder(8)]
    [JsonPropertyName("expiresAtUtc")]
    public DateTime ExpiresAtUtc { get; set; }
}

public static class EmergencyLoginTokenCodec
{
    public const string TokenPrefix = "HBPOSE1";
    public const string WpfAudience = "Hbpos.Wpf";
    public const string AllPosTerminalProfile = "AllPosTerminal";
    public const int MaxTokenLength = 2048;

    private const int P256SignatureLength = 64;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Sign(
        EmergencyLoginTokenPayload payload,
        string keyId,
        string privateKeyPem
    )
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidateKeyId(keyId);
        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            throw new ArgumentException("紧急登录私钥未配置", nameof(privateKeyPem));
        }

        payload.PermissionProfile = AllPosTerminalProfile;
        payload.Audience = WpfAudience;
        payload.IssuedAtUtc = payload.IssuedAtUtc.ToUniversalTime();
        payload.NotBeforeUtc = payload.NotBeforeUtc.ToUniversalTime();
        payload.ExpiresAtUtc = payload.ExpiresAtUtc.ToUniversalTime();

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        if (key.KeySize != 256)
        {
            throw new ArgumentException("紧急登录签名密钥必须使用 ECDSA P-256", nameof(privateKeyPem));
        }

        var signature = key.SignData(
            BuildSignedBytes(keyId, payloadBytes),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation
        );

        var token = $"{TokenPrefix}-{keyId}-{Convert.ToHexString(payloadBytes)}-{Convert.ToHexString(signature)}";
        if (token.Length > MaxTokenLength)
        {
            throw new ArgumentException($"紧急登录令牌不能超过 {MaxTokenLength} 个字符", nameof(payload));
        }

        return token;
    }

    public static bool TryVerify(
        string? token,
        IReadOnlyDictionary<string, string> publicKeysById,
        DateTime utcNow,
        out EmergencyLoginTokenPayload? payload,
        out string errorCode
    )
    {
        payload = null;
        errorCode = "EMERGENCY_TOKEN_INVALID";
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
        {
            return false;
        }

        var parts = token.Split('-', 4, StringSplitOptions.None);
        if (
            parts.Length != 4
            || !parts[0].Equals(TokenPrefix, StringComparison.Ordinal)
            || !IsValidKeyId(parts[1])
        )
        {
            errorCode = "EMERGENCY_TOKEN_FORMAT_INVALID";
            return false;
        }

        if (
            !publicKeysById.TryGetValue(parts[1], out var publicKeyPem)
            || string.IsNullOrWhiteSpace(publicKeyPem)
        )
        {
            errorCode = "EMERGENCY_TOKEN_KEY_UNKNOWN";
            return false;
        }

        try
        {
            var payloadBytes = Convert.FromHexString(parts[2]);
            var signature = Convert.FromHexString(parts[3]);
            if (signature.Length != P256SignatureLength)
            {
                return false;
            }

            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            if (key.KeySize != 256)
            {
                errorCode = "EMERGENCY_TOKEN_KEY_INVALID";
                return false;
            }

            if (
                !key.VerifyData(
                    BuildSignedBytes(parts[1], payloadBytes),
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation
                )
            )
            {
                errorCode = "EMERGENCY_TOKEN_SIGNATURE_INVALID";
                return false;
            }

            payload = JsonSerializer.Deserialize<EmergencyLoginTokenPayload>(payloadBytes, JsonOptions);
            if (
                payload == null
                || payload.GrantId == Guid.Empty
                || string.IsNullOrWhiteSpace(payload.StoreCode)
                || payload.StoreCode.Length > 50
                || payload.StoreCode != payload.StoreCode.Trim()
                || string.IsNullOrWhiteSpace(payload.BusinessDate)
                || !DateOnly.TryParseExact(
                    payload.BusinessDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _
                )
                || string.IsNullOrWhiteSpace(payload.Issuer)
                || payload.Issuer.Length > 128
                || !string.Equals(
                    payload.PermissionProfile,
                    AllPosTerminalProfile,
                    StringComparison.Ordinal
                )
                || !string.Equals(payload.Audience, WpfAudience, StringComparison.Ordinal)
                || payload.IssuedAtUtc > payload.ExpiresAtUtc
                || payload.ExpiresAtUtc <= payload.NotBeforeUtc
            )
            {
                payload = null;
                errorCode = "EMERGENCY_TOKEN_PAYLOAD_INVALID";
                return false;
            }

            var now = utcNow.ToUniversalTime();
            if (now < payload.NotBeforeUtc.ToUniversalTime())
            {
                payload = null;
                errorCode = "EMERGENCY_TOKEN_NOT_ACTIVE";
                return false;
            }

            if (now >= payload.ExpiresAtUtc.ToUniversalTime())
            {
                payload = null;
                errorCode = "EMERGENCY_TOKEN_EXPIRED";
                return false;
            }

            errorCode = string.Empty;
            return true;
        }
        catch (Exception ex) when (
            ex is FormatException
            or JsonException
            or CryptographicException
            or ArgumentException
        )
        {
            payload = null;
            return false;
        }
    }

    private static byte[] BuildSignedBytes(string keyId, byte[] payloadBytes)
    {
        // 关键逻辑：把版本和 KeyId 一并纳入签名，防止令牌被改挂到另一把轮换密钥。
        var headerBytes = Encoding.ASCII.GetBytes($"{TokenPrefix}-{keyId}-");
        var signedBytes = new byte[headerBytes.Length + payloadBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, signedBytes, 0, headerBytes.Length);
        Buffer.BlockCopy(payloadBytes, 0, signedBytes, headerBytes.Length, payloadBytes.Length);
        return signedBytes;
    }

    private static void ValidateKeyId(string keyId)
    {
        if (!IsValidKeyId(keyId))
        {
            throw new ArgumentException("KeyId 仅允许 1-32 位 ASCII 字母或数字", nameof(keyId));
        }
    }

    private static bool IsValidKeyId(string? keyId) =>
        !string.IsNullOrEmpty(keyId)
        && keyId.Length <= 32
        && keyId.All(character =>
            character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
        );
}
