using Hbpos.Contracts.Linkly;

namespace Hbpos.Client.Wpf.Services;

internal static class LinklyLocalTxnRef
{
    // 派生算法与 Hbpos.Api 共用同一份实现：后端异步链路由 API 按同一 attempt 身份算出同一个引用。
    internal static string Create(char transactionType, string stableIdentity)
    {
        return LinklyAttemptTxnRef.Create(transactionType, stableIdentity);
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
