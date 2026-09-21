using System.Security.Cryptography;
using System.Text;

namespace Hbpos.Contracts.Linkly;

/// <summary>
/// 由 POS 本地 attempt 身份派生 Linkly 的 16 位 TxnRef：交易类型字符 + SHA-256 摘要前 75 位的 Base32。
/// POS 与 Hbpos.Api 必须得到同一个值——POS 在发请求前就把它落到本地 attempt，后端异步链路由 API 按同一
/// attempt 身份派生后送终端，所以算法只能有这一份实现，改动即破坏两端已落库记录的对应关系。
/// </summary>
public static class LinklyAttemptTxnRef
{
    private const string HashInputPrefix = "HBPOS-LINKLY-TXNREF-V1";
    private const string Base32Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Create(char transactionType, Guid attemptGuid)
    {
        if (attemptGuid == Guid.Empty)
        {
            throw new ArgumentException("Attempt identity must not be empty.", nameof(attemptGuid));
        }

        return Create(transactionType, attemptGuid.ToString("D"));
    }

    public static string Create(char transactionType, string stableIdentity)
    {
        if (transactionType is not ('P' or 'R'))
        {
            throw new ArgumentOutOfRangeException(nameof(transactionType), transactionType, "Linkly transaction type must be P or R.");
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
}
