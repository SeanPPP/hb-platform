using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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

public sealed record EmergencyLoginVerifiedClaims(
    Guid GrantId,
    string StoreCode,
    DateTime NotBeforeUtc,
    DateTime ExpiresAtUtc);

public static class EmergencyLoginTokenCodec
{
    public const string TokenPrefix = "HBPOSE1";
    public const string LegacyTokenPrefix = TokenPrefix;
    public const string V2TokenPrefix = "HBPOSE2";
    public const string WpfAudience = "Hbpos.Wpf";
    public const string AllPosTerminalProfile = "AllPosTerminal";
    public const int MaxTokenLength = 2048;
    public const int V2TokenLength = 158;

    private const int P256SignatureLength = 64;
    private const int V2ClaimsLength = 48;
    private const int V2DecodedLength = V2ClaimsLength + P256SignatureLength;
    private const int V2KeySelectorLength = 8;
    private const int V2StoreFingerprintLength = 16;
    private const string V2SignedPrefix = "HBPOSE2-";
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
        if (!IsP256(key))
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

    public static string SignLegacy(
        EmergencyLoginTokenPayload payload,
        string keyId,
        string privateKeyPem) => Sign(payload, keyId, privateKeyPem);

    public static string SignV2(
        EmergencyLoginVerifiedClaims claims,
        string keyId,
        string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(claims);
        return SignV2(
            claims.GrantId,
            claims.StoreCode,
            claims.NotBeforeUtc,
            claims.ExpiresAtUtc,
            keyId,
            privateKeyPem);
    }

    public static string SignV2(
        Guid grantId,
        string storeCode,
        DateTime notBeforeUtc,
        DateTime expiresAtUtc,
        string keyId,
        string privateKeyPem)
    {
        if (grantId == Guid.Empty)
        {
            throw new ArgumentException("紧急登录授权编号不能为空", nameof(grantId));
        }

        ValidateKeyId(keyId);
        var normalizedStoreCode = NormalizeStoreCode(storeCode);
        var notBeforeSeconds = ToV2UnixSeconds(notBeforeUtc, nameof(notBeforeUtc));
        var expiresAtSeconds = ToV2UnixSeconds(expiresAtUtc, nameof(expiresAtUtc));
        if (expiresAtSeconds <= notBeforeSeconds)
        {
            throw new ArgumentException("紧急登录令牌过期时间必须晚于生效时间", nameof(expiresAtUtc));
        }

        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            throw new ArgumentException("紧急登录私钥未配置", nameof(privateKeyPem));
        }

        Span<byte> claims = stackalloc byte[V2ClaimsLength];
        WriteKeySelector(keyId, claims[..V2KeySelectorLength]);
        if (!grantId.TryWriteBytes(claims.Slice(8, 16), bigEndian: true, out var written) || written != 16)
        {
            throw new ArgumentException("紧急登录授权编号编码失败", nameof(grantId));
        }

        WriteStoreFingerprint(normalizedStoreCode, claims.Slice(24, V2StoreFingerprintLength));
        BinaryPrimitives.WriteUInt32BigEndian(claims.Slice(40, 4), notBeforeSeconds);
        BinaryPrimitives.WriteUInt32BigEndian(claims.Slice(44, 4), expiresAtSeconds);

        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        if (!IsP256(key))
        {
            throw new ArgumentException("紧急登录签名密钥必须使用 ECDSA P-256", nameof(privateKeyPem));
        }

        // 关键逻辑：版本前缀与紧凑 claims 一同签名，防止跨版本替换或字段拆装。
        var signature = key.SignData(
            BuildV2SignedBytes(claims),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        if (signature.Length != P256SignatureLength)
        {
            throw new CryptographicException("紧急登录签名长度无效");
        }

        var encoded = new byte[V2DecodedLength];
        claims.CopyTo(encoded);
        signature.CopyTo(encoded.AsSpan(V2ClaimsLength));
        var token = V2SignedPrefix + Base64UrlEncode(encoded);
        if (token.Length != V2TokenLength)
        {
            throw new CryptographicException("紧急登录紧凑令牌长度无效");
        }

        return token;
    }

    public static bool HasSupportedPrefix(string? token) =>
        token?.StartsWith($"{LegacyTokenPrefix}-", StringComparison.Ordinal) == true
        || token?.StartsWith(V2SignedPrefix, StringComparison.Ordinal) == true;

    public static bool TryVerify(
        string? token,
        IReadOnlyDictionary<string, string> publicKeysById,
        string expectedStoreCode,
        DateTime utcNow,
        out EmergencyLoginVerifiedClaims? claims,
        out string errorCode)
    {
        claims = null;
        if (token?.StartsWith(V2SignedPrefix, StringComparison.Ordinal) == true)
        {
            return TryVerifyV2(
                token,
                publicKeysById,
                expectedStoreCode,
                utcNow,
                out claims,
                out errorCode);
        }

        if (token?.StartsWith($"{LegacyTokenPrefix}-", StringComparison.Ordinal) != true)
        {
            errorCode = "EMERGENCY_TOKEN_FORMAT_INVALID";
            return false;
        }

        if (!TryVerify(token, publicKeysById, utcNow, out var payload, out errorCode))
        {
            return false;
        }

        var normalizedExpectedStore = NormalizeStoreCodeForVerification(expectedStoreCode);
        if (normalizedExpectedStore == null ||
            !string.Equals(payload!.StoreCode, normalizedExpectedStore, StringComparison.OrdinalIgnoreCase))
        {
            errorCode = "EMERGENCY_TOKEN_WRONG_STORE";
            return false;
        }

        claims = new EmergencyLoginVerifiedClaims(
            payload.GrantId,
            payload.StoreCode,
            payload.NotBeforeUtc.ToUniversalTime(),
            payload.ExpiresAtUtc.ToUniversalTime());
        errorCode = string.Empty;
        return true;
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
            if (!IsP256(key))
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

    private static bool TryVerifyV2(
        string token,
        IReadOnlyDictionary<string, string> publicKeysById,
        string expectedStoreCode,
        DateTime utcNow,
        out EmergencyLoginVerifiedClaims? claims,
        out string errorCode)
    {
        claims = null;
        errorCode = "EMERGENCY_TOKEN_FORMAT_INVALID";
        if (token.Length != V2TokenLength)
        {
            return false;
        }

        var encoded = token.AsSpan(V2SignedPrefix.Length);
        if (!IsBase64Url(encoded))
        {
            return false;
        }

        byte[] decoded;
        try
        {
            decoded = Base64UrlDecode(encoded);
        }
        catch (FormatException)
        {
            return false;
        }

        if (decoded.Length != V2DecodedLength ||
            !encoded.SequenceEqual(Base64UrlEncode(decoded).AsSpan()))
        {
            return false;
        }

        var body = decoded.AsSpan(0, V2ClaimsLength);
        var signature = decoded.AsSpan(V2ClaimsLength, P256SignatureLength);
        var grantId = new Guid(body.Slice(8, 16), bigEndian: true);
        var notBeforeSeconds = BinaryPrimitives.ReadUInt32BigEndian(body.Slice(40, 4));
        var expiresAtSeconds = BinaryPrimitives.ReadUInt32BigEndian(body.Slice(44, 4));
        if (grantId == Guid.Empty || expiresAtSeconds <= notBeforeSeconds)
        {
            errorCode = "EMERGENCY_TOKEN_PAYLOAD_INVALID";
            return false;
        }

        var matchingKeys = new List<KeyValuePair<string, string>>(2);
        // 关键逻辑：选择器只能命中唯一 KeyId；碰撞或重复配置必须失败关闭。
        foreach (var pair in publicKeysById)
        {
            if (IsValidKeyId(pair.Key) && KeySelectorMatches(pair.Key, body[..V2KeySelectorLength]))
            {
                matchingKeys.Add(pair);
                if (matchingKeys.Count == 2)
                {
                    break;
                }
            }
        }
        if (matchingKeys.Count == 0)
        {
            errorCode = "EMERGENCY_TOKEN_KEY_UNKNOWN";
            return false;
        }

        if (matchingKeys.Count != 1 || string.IsNullOrWhiteSpace(matchingKeys[0].Value))
        {
            errorCode = "EMERGENCY_TOKEN_KEY_INVALID";
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(matchingKeys[0].Value);
            if (!IsP256(key))
            {
                errorCode = "EMERGENCY_TOKEN_KEY_INVALID";
                return false;
            }

            if (!key.VerifyData(
                    BuildV2SignedBytes(body),
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                errorCode = "EMERGENCY_TOKEN_SIGNATURE_INVALID";
                return false;
            }
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            errorCode = "EMERGENCY_TOKEN_KEY_INVALID";
            return false;
        }

        var normalizedStoreCode = NormalizeStoreCodeForVerification(expectedStoreCode);
        if (normalizedStoreCode == null)
        {
            errorCode = "EMERGENCY_TOKEN_WRONG_STORE";
            return false;
        }

        Span<byte> expectedFingerprint = stackalloc byte[V2StoreFingerprintLength];
        WriteStoreFingerprint(normalizedStoreCode, expectedFingerprint);
        // 关键逻辑：门店指纹使用固定时间比较，避免从比较耗时泄漏匹配前缀。
        if (!CryptographicOperations.FixedTimeEquals(
                expectedFingerprint,
                body.Slice(24, V2StoreFingerprintLength)))
        {
            errorCode = "EMERGENCY_TOKEN_WRONG_STORE";
            return false;
        }

        var notBeforeUtc = DateTimeOffset.FromUnixTimeSeconds(notBeforeSeconds).UtcDateTime;
        var expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expiresAtSeconds).UtcDateTime;
        var now = utcNow.ToUniversalTime();
        if (now < notBeforeUtc)
        {
            errorCode = "EMERGENCY_TOKEN_NOT_ACTIVE";
            return false;
        }

        if (now >= expiresAtUtc)
        {
            errorCode = "EMERGENCY_TOKEN_EXPIRED";
            return false;
        }

        claims = new EmergencyLoginVerifiedClaims(
            grantId,
            normalizedStoreCode,
            notBeforeUtc,
            expiresAtUtc);
        errorCode = string.Empty;
        return true;
    }

    private static byte[] BuildV2SignedBytes(ReadOnlySpan<byte> claims)
    {
        var prefix = Encoding.ASCII.GetBytes(V2SignedPrefix);
        var signedBytes = new byte[prefix.Length + claims.Length];
        prefix.CopyTo(signedBytes, 0);
        claims.CopyTo(signedBytes.AsSpan(prefix.Length));
        return signedBytes;
    }

    private static void WriteKeySelector(string keyId, Span<byte> destination)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(keyId));
        hash.AsSpan(0, V2KeySelectorLength).CopyTo(destination);
    }

    private static bool KeySelectorMatches(string keyId, ReadOnlySpan<byte> selector)
    {
        Span<byte> candidate = stackalloc byte[V2KeySelectorLength];
        WriteKeySelector(keyId, candidate);
        return CryptographicOperations.FixedTimeEquals(candidate, selector);
    }

    private static void WriteStoreFingerprint(string normalizedStoreCode, Span<byte> destination)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedStoreCode));
        hash.AsSpan(0, V2StoreFingerprintLength).CopyTo(destination);
    }

    private static string NormalizeStoreCode(string storeCode) =>
        NormalizeStoreCodeForVerification(storeCode)
        ?? throw new ArgumentException("门店代码不能为空", nameof(storeCode));

    private static string? NormalizeStoreCodeForVerification(string? storeCode)
    {
        var normalized = storeCode?.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > 50 ? null : normalized;
    }

    private static uint ToV2UnixSeconds(DateTime value, string parameterName)
    {
        var seconds = new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeSeconds();
        if (seconds < 0 || seconds > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, "紧急登录时间超出 V2 可编码范围");
        }

        return (uint)seconds;
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(ReadOnlySpan<char> encoded)
    {
        var base64 = encoded.ToString().Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }

    private static bool IsBase64Url(ReadOnlySpan<char> encoded)
    {
        foreach (var character in encoded)
        {
            if (character is not (>= 'A' and <= 'Z')
                and not (>= 'a' and <= 'z')
                and not (>= '0' and <= '9')
                and not '-' and not '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsP256(ECDsa key)
    {
        var curveOid = key.ExportParameters(false).Curve.Oid.Value;
        return key.KeySize == 256
            && string.Equals(
                curveOid,
                ECCurve.NamedCurves.nistP256.Oid.Value,
                StringComparison.Ordinal);
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
