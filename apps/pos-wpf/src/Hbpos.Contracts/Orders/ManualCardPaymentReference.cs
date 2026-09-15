namespace Hbpos.Contracts.Orders;

/// <summary>独立刷卡机的人工确认标识；不得作为 Linkly 或 Square 的交易引用发送。</summary>
public static class ManualCardPaymentReference
{
    public const string Prefix = "MANUAL:";
    public const string Processor = "Manual";

    public static string Format(Guid confirmationId)
    {
        if (confirmationId == Guid.Empty)
        {
            throw new ArgumentException("Manual confirmation identity is required.", nameof(confirmationId));
        }

        return $"{Prefix}{confirmationId:N}";
    }

    // 中文注释：防护判断包括损坏的手动引用，不能因 GUID 格式错误而回退到自动终端。
    public static bool IsManual(string? reference) =>
        reference?.Trim().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) == true;

    public static bool TryParse(string? reference, out Guid confirmationId)
    {
        confirmationId = Guid.Empty;
        return IsManual(reference) &&
            Guid.TryParse(reference!.Trim()[Prefix.Length..], out confirmationId) &&
            confirmationId != Guid.Empty;
    }

    public static bool IsManualRefundSource(string? reference) =>
        IsManual(reference) ||
        (CardRefundReference.TryGetOriginalReference(reference, out var original) && IsManual(original));
}
