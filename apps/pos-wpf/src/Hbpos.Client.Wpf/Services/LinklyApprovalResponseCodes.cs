namespace Hbpos.Client.Wpf.Services;

internal static class LinklyApprovalResponseCodes
{
    public static bool IsApproved(string? responseCode)
    {
        var code = string.IsNullOrWhiteSpace(responseCode) ? null : responseCode.Trim();
        // Linkly/ANZ 的 08 是签名核验批准；所有 Linkly 云路径都必须进入成功链路。
        return string.Equals(code, "00", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(code, "08", StringComparison.OrdinalIgnoreCase);
    }
}
