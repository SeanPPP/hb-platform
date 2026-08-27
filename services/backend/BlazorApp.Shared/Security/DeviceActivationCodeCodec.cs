using System.Security.Cryptography;
using System.Numerics;

namespace BlazorApp.Shared.Security;

public sealed record DeviceActivationCodeMaterial(
    Guid GrantId,
    string ActivationCode,
    byte[] SecretHash);

public sealed record ParsedDeviceActivationCode(Guid GrantId, byte[] Secret);

/// <summary>
/// 设备开通码仅是一次性 bearer secret；数据库只保存摘要，明文只在创建响应中出现一次。
/// </summary>
public static class DeviceActivationCodeCodec
{
    private const string Prefix = "HBDEV1";
    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int EncodedPartLength = 26;
    private const int SecretByteLength = 16;
    private const int ActivationCodeLength = 6 + 2 + (EncodedPartLength * 2);

    public static DeviceActivationCodeMaterial Create()
    {
        var grantId = Guid.NewGuid();
        var secret = RandomNumberGenerator.GetBytes(SecretByteLength);
        Span<byte> grantBytes = stackalloc byte[16];
        grantId.TryWriteBytes(grantBytes, bigEndian: true, out _);
        var activationCode = $"{Prefix}-{EncodeCrockford(grantBytes)}-{EncodeCrockford(secret)}";
        return new DeviceActivationCodeMaterial(grantId, activationCode, Hash(secret));
    }

    public static bool TryParse(string? activationCode, out ParsedDeviceActivationCode parsed)
    {
        parsed = new ParsedDeviceActivationCode(Guid.Empty, Array.Empty<byte>());
        var compact = activationCode == null
            ? null
            : string.Concat(activationCode.Where(character =>
                character is not (' ' or '\t' or '\n' or '\v' or '\f' or '\r')));
        if (compact?.Any(character => character > 0x7f) == true)
        {
            return false;
        }
        var candidate = compact?.ToUpperInvariant();
        if (string.IsNullOrEmpty(candidate) || candidate.Length > 128)
        {
            return false;
        }

        var parts = candidate.Split('-', StringSplitOptions.None);
        if (parts.Length != 3
            || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)
            || !TryDecodeCrockford(parts[1], out var grantBytes)
            || !TryDecodeCrockford(parts[2], out var secret))
        {
            return false;
        }

        parsed = new ParsedDeviceActivationCode(new Guid(grantBytes, bigEndian: true), secret);
        return true;
    }

    public static bool Matches(ReadOnlySpan<byte> expectedHash, ReadOnlySpan<byte> secret)
    {
        if (expectedHash.Length != SHA256.HashSizeInBytes)
        {
            return false;
        }

        Span<byte> actualHash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(secret, actualHash);
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

    /// <summary>
    /// 检测公开元数据中是否误放了完整开通码。检测时兼容扫码输入中的 ASCII 空白，
    /// 但绝不把匹配到的 bearer secret 返回给调用方。
    /// </summary>
    public static bool ContainsReservedActivationCode(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var compact = string.Concat(value.Where(character =>
            character is not (' ' or '\t' or '\n' or '\v' or '\f' or '\r')));
        if (compact.Length < ActivationCodeLength)
        {
            return false;
        }

        for (var index = 0; index <= compact.Length - ActivationCodeLength; index++)
        {
            if (!compact.AsSpan(index, Prefix.Length).Equals(
                    Prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryParse(compact.Substring(index, ActivationCodeLength), out _))
            {
                return true;
            }
        }

        return false;
    }

    public static string? RedactReservedActivationMetadata(string? value) =>
        ContainsReservedActivationCode(value) ? "[REDACTED]" : value;

    /// <summary>
    /// SQL Server datetime2 读回时 Kind 为 Unspecified；这些字段语义固定为 UTC，
    /// 必须在 JSON 序列化前恢复 UTC Kind，确保 wire format 始终带 Z。
    /// </summary>
    public static DateTime NormalizeUtcForWire(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    public static DateTime? NormalizeUtcForWire(DateTime? value) =>
        value.HasValue ? NormalizeUtcForWire(value.Value) : null;

    private static byte[] Hash(ReadOnlySpan<byte> secret) => SHA256.HashData(secret);

    private static string EncodeCrockford(ReadOnlySpan<byte> value)
    {
        var number = new BigInteger(value, isUnsigned: true, isBigEndian: true);
        Span<char> encoded = stackalloc char[EncodedPartLength];
        for (var index = encoded.Length - 1; index >= 0; index--)
        {
            number = BigInteger.DivRem(number, 32, out var remainder);
            encoded[index] = CrockfordAlphabet[(int)remainder];
        }
        return new string(encoded);
    }

    private static bool TryDecodeCrockford(string value, out byte[] decoded)
    {
        decoded = Array.Empty<byte>();
        if (value.Length != EncodedPartLength || value[0] > '7')
        {
            return false;
        }

        var number = BigInteger.Zero;
        foreach (var character in value)
        {
            var digit = CrockfordAlphabet.IndexOf(character);
            if (digit < 0)
            {
                return false;
            }
            number = number * 32 + digit;
        }

        var bytes = number.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length > SecretByteLength)
        {
            return false;
        }

        decoded = new byte[SecretByteLength];
        bytes.CopyTo(decoded, decoded.Length - bytes.Length);
        return string.Equals(value, EncodeCrockford(decoded), StringComparison.Ordinal);
    }
}
