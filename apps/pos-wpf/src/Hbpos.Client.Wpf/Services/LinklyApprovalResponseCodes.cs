namespace Hbpos.Client.Wpf.Services;

internal static class LinklyApprovalResponseCodes
{
    public static bool IsApproved(string? responseCode)
    {
        var code = string.IsNullOrWhiteSpace(responseCode) ? null : responseCode.Trim();
        // Linkly 批准码统一在这里维护：00 为普通批准，08 为签名核验批准，11 为 Approved VIP。
        return string.Equals(code, "00", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(code, "08", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(code, "11", StringComparison.OrdinalIgnoreCase);
    }
}
