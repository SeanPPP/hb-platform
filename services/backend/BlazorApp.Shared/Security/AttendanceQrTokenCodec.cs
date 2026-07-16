using System.Security.Cryptography;
using System.Text;

namespace BlazorApp.Shared.Security;

public sealed class AttendanceQrTokenPayload
{
    public Guid TokenId { get; set; }
    public string StoreCode { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }

    // 兼容现有调用方；这些字段不进入密文，服务端有效期始终由签发时间和统一常量推导。
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public DateTime NotBeforeUtc { get; set; }
    public DateTime ExpiresAtUtc => IssuedAtUtc.ToUniversalTime().AddSeconds(AttendanceQrTokenCodec.TokenLifetimeSeconds);
}

public static class AttendanceQrTokenCodec
{
    public const int TokenLifetimeSeconds = 15;
    public const string TokenPrefix = "HBATE1";
    public const int MaxTokenLength = 600;
    public const int KeyLength = 32;

    private const byte PayloadFormatVersion = 1;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int MaxCodeCharacters = 50;
    private const int MaxCodeBytes = 150;
    private const int MinimumPayloadLength = 29;
    private const int MaximumPayloadLength = 327;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromSeconds(TokenLifetimeSeconds);
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public static string Encrypt(AttendanceQrTokenPayload payload, string keyId, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidateKeyId(keyId);
        ValidateKey(key);
        NormalizeAndValidatePayload(payload);

        var plaintext = EncodePayload(payload);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        using (var aes = new AesGcm(key, TagLength))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, BuildAad(keyId));
        }

        var token = string.Join('.', TokenPrefix, keyId, Base64UrlEncode(nonce),
            Base64UrlEncode(ciphertext), Base64UrlEncode(tag));
        if (token.Length > MaxTokenLength)
        {
            throw new ArgumentException($"考勤二维码令牌不能超过 {MaxTokenLength} 个字符", nameof(payload));
        }

        return token;
    }

    public static bool TryGetKeyId(string? token, out string keyId, out string errorCode)
    {
        keyId = string.Empty;
        errorCode = "ATTENDANCE_QR_FORMAT_INVALID";
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
        {
            return false;
        }

        var parts = token.Split('.', StringSplitOptions.None);
        if (parts.Length != 5
            || !parts[0].Equals(TokenPrefix, StringComparison.Ordinal)
            || !IsValidKeyId(parts[1])
            || !IsCanonicalBase64Url(parts[2], NonceLength, NonceLength)
            || !IsCanonicalBase64Url(parts[3], MinimumPayloadLength, MaximumPayloadLength)
            || !IsCanonicalBase64Url(parts[4], TagLength, TagLength))
        {
            return false;
        }

        keyId = parts[1];
        errorCode = string.Empty;
        return true;
    }

    public static bool TryDecrypt(
        string? token,
        IReadOnlyDictionary<string, byte[]> keysById,
        DateTime utcNow,
        out AttendanceQrTokenPayload? payload,
        out string keyId,
        out string errorCode)
    {
        payload = null;
        if (!TryGetKeyId(token, out keyId, out errorCode))
        {
            return false;
        }

        if (!keysById.TryGetValue(keyId, out var key))
        {
            errorCode = "ATTENDANCE_QR_KEY_UNKNOWN";
            return false;
        }

        if (!TryDecryptIdentity(token, key, out payload, out _, out errorCode))
        {
            return false;
        }

        if (!TryValidateLifetime(payload!, utcNow, out errorCode))
        {
            payload = null;
            return false;
        }

        return true;
    }

    public static bool TryDecryptIdentity(
        string? token,
        byte[] key,
        out AttendanceQrTokenPayload? payload,
        out string keyId,
        out string errorCode)
    {
        payload = null;
        if (!TryGetKeyId(token, out keyId, out errorCode))
        {
            return false;
        }

        if (key is null || key.Length != KeyLength)
        {
            errorCode = "ATTENDANCE_QR_KEY_INVALID";
            return false;
        }

        try
        {
            var parts = token!.Split('.', StringSplitOptions.None);
            var nonce = Base64UrlDecode(parts[2]);
            var ciphertext = Base64UrlDecode(parts[3]);
            var tag = Base64UrlDecode(parts[4]);
            var plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(key, TagLength))
            {
                // 关键逻辑：AAD 绑定协议版本和 kid，替换任一头字段都会导致认证失败。
                aes.Decrypt(nonce, ciphertext, tag, plaintext, BuildAad(keyId));
            }

            payload = DecodePayload(plaintext);
            if (!IsValidPayload(payload))
            {
                payload = null;
                errorCode = "ATTENDANCE_QR_PAYLOAD_INVALID";
                return false;
            }

            errorCode = string.Empty;
            return true;
        }
        catch (AuthenticationTagMismatchException)
        {
            errorCode = "ATTENDANCE_QR_AUTH_INVALID";
            return false;
        }
        catch (Exception exception) when (exception is FormatException
            or EndOfStreamException
            or DecoderFallbackException
            or CryptographicException
            or ArgumentException
            or OverflowException)
        {
            payload = null;
            errorCode = "ATTENDANCE_QR_FORMAT_INVALID";
            return false;
        }
    }

    public static bool TryValidateLifetime(
        AttendanceQrTokenPayload payload,
        DateTime utcNow,
        out string errorCode)
    {
        var now = utcNow.ToUniversalTime();
        var issuedAt = payload.IssuedAtUtc.ToUniversalTime();
        if (now < issuedAt)
        {
            errorCode = "ATTENDANCE_QR_NOT_ACTIVE";
            return false;
        }

        if (now >= issuedAt.Add(TokenLifetime))
        {
            errorCode = "ATTENDANCE_QR_EXPIRED";
            return false;
        }

        errorCode = string.Empty;
        return true;
    }

    private static byte[] EncodePayload(AttendanceQrTokenPayload payload)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true);
        writer.Write(PayloadFormatVersion);
        writer.Write(payload.TokenId.ToByteArray());
        writer.Write(new DateTimeOffset(payload.IssuedAtUtc).ToUnixTimeMilliseconds());
        WriteUtf8(writer, payload.StoreCode);
        WriteUtf8(writer, payload.DeviceCode);
        writer.Flush();
        return stream.ToArray();
    }

    private static AttendanceQrTokenPayload DecodePayload(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, StrictUtf8, leaveOpen: true);
        if (reader.ReadByte() != PayloadFormatVersion)
        {
            throw new FormatException("考勤二维码 payload 版本无效");
        }

        var tokenIdBytes = reader.ReadBytes(16);
        if (tokenIdBytes.Length != 16)
        {
            throw new EndOfStreamException();
        }

        var issuedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64()).UtcDateTime;
        var storeCode = ReadUtf8(reader);
        var deviceCode = ReadUtf8(reader);
        if (stream.Position != stream.Length)
        {
            throw new FormatException("考勤二维码 payload 包含尾随数据");
        }

        return new AttendanceQrTokenPayload
        {
            TokenId = new Guid(tokenIdBytes),
            StoreCode = storeCode,
            DeviceCode = deviceCode,
            IssuedAtUtc = issuedAt,
        };
    }

    private static void WriteUtf8(BinaryWriter writer, string value)
    {
        var bytes = StrictUtf8.GetBytes(value);
        if (bytes.Length > MaxCodeBytes)
        {
            throw new ArgumentException("考勤二维码字段编码后过长");
        }

        writer.Write(checked((byte)bytes.Length));
        writer.Write(bytes);
    }

    private static string ReadUtf8(BinaryReader reader)
    {
        var length = reader.ReadByte();
        if (length == 0 || length > MaxCodeBytes)
        {
            throw new FormatException("考勤二维码字段长度无效");
        }

        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new EndOfStreamException();
        }

        return StrictUtf8.GetString(bytes);
    }

    private static void NormalizeAndValidatePayload(AttendanceQrTokenPayload payload)
    {
        payload.IssuedAtUtc = payload.IssuedAtUtc.ToUniversalTime();
        if (!IsValidPayload(payload))
        {
            throw new ArgumentException("考勤二维码载荷无效", nameof(payload));
        }
    }

    private static bool IsValidPayload(AttendanceQrTokenPayload? payload) =>
        payload is not null
        && payload.TokenId != Guid.Empty
        && IsTrimmedValue(payload.StoreCode)
        && IsTrimmedValue(payload.DeviceCode)
        && payload.IssuedAtUtc.Kind == DateTimeKind.Utc;

    private static bool IsTrimmedValue(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaxCodeCharacters
        && value == value.Trim()
        && StrictUtf8.GetByteCount(value) <= MaxCodeBytes;

    private static byte[] BuildAad(string keyId) =>
        Encoding.ASCII.GetBytes($"{TokenPrefix}.{keyId}");

    private static void ValidateKey(byte[] key)
    {
        if (key is null || key.Length != KeyLength)
        {
            throw new ArgumentException("考勤二维码密钥必须为 32 字节", nameof(key));
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            0 => padded,
            2 => padded + "==",
            3 => padded + "=",
            _ => throw new FormatException("Base64Url 长度无效"),
        };
        return Convert.FromBase64String(padded);
    }

    private static void ValidateKeyId(string keyId)
    {
        if (!IsValidKeyId(keyId))
        {
            throw new ArgumentException("kid 仅允许 1-64 位 ASCII 字母、数字、短横线或下划线", nameof(keyId));
        }
    }

    private static bool IsValidKeyId(string? keyId) =>
        !string.IsNullOrEmpty(keyId)
        && keyId.Length <= 64
        && keyId.All(character => character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '-'
            or '_');

    private static bool IsCanonicalBase64Url(string value, int minimumBytes, int maximumBytes)
    {
        if (string.IsNullOrEmpty(value)
            || value.Any(character => character is not (>= 'A' and <= 'Z')
                and not (>= 'a' and <= 'z')
                and not (>= '0' and <= '9')
                and not '-'
                and not '_'))
        {
            return false;
        }

        try
        {
            var bytes = Base64UrlDecode(value);
            return bytes.Length >= minimumBytes
                && bytes.Length <= maximumBytes
                && Base64UrlEncode(bytes).Equals(value, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
