using System.Security.Cryptography;
using System.Text;

namespace Hbpos.Client.Wpf.Services;

internal static class LinklyLocalTxnRef
{
    private const string HashInputPrefix = "HBPOS-LINKLY-TXNREF-V1";
    private const string Base32Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    internal static string Create(char transactionType, string stableIdentity)
    {
        if (transactionType is not ('P' or 'R'))
        {
            throw new ArgumentOutOfRangeException(nameof(transactionType), transactionType, "Linkly Local IP transaction type must be P or R.");
        }

        ArgumentException.ThrowIfNullOrEmpty(stableIdentity);

        var input = $"{HashInputPrefix}|{transactionType}|{stableIdentity}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        Span<char> result = stackalloc char[16];
        result[0] = transactionType;
        for (var index = 0; index < 15; index++)
        {
            var value = 0;
            var bitOffset = index * 5;
            for (var bit = 0; bit < 5; bit++)
            {
                var absoluteBit = bitOffset + bit;
                var byteValue = digest[absoluteBit / 8];
                var bitValue = (byteValue >> (7 - (absoluteBit % 8))) & 1;
                value = (value << 1) | bitValue;
            }

            result[index + 1] = Base32Alphabet[value];
        }

        return new string(result);
    }

    internal static bool TryNormalizeHistoricalReference(string? reference, out string normalized)
    {
        normalized = string.Empty;
        if (reference is null)
        {
            return false;
        }

        // 中文注释：协议允许首尾空格作为填充，但所有控制字符和非 ASCII 字符必须在接触 SDK 前拒绝。
        foreach (var character in reference)
        {
            if (character is < '\x20' or > '\x7E')
            {
                return false;
            }
        }

        var trimmed = reference.Trim(' ');
        normalized = trimmed.StartsWith("ANZ:", StringComparison.OrdinalIgnoreCase)
            ? trimmed[4..].Trim(' ')
            : trimmed;
        return normalized.Length is >= 1 and <= 16;
    }

    internal static string? TrimProtocolPadding(string? reference)
    {
        return reference?.Trim(' ');
    }
}
