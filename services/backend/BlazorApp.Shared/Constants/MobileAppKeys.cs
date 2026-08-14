namespace BlazorApp.Shared.Constants;

/// <summary>
/// 服务端受控的移动应用数据分区键。外部 webhook 只能通过已配置的 EAS project 映射获得该值。
/// </summary>
public static class MobileAppKeys
{
    public const string Mobile = "mobile";

    public const string PosHandheld = "pos-handheld";

    public static bool TryNormalize(string? value, out string normalized)
    {
        var candidate = (value ?? string.Empty).Trim();
        if (string.Equals(candidate, Mobile, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Mobile;
            return true;
        }

        if (string.Equals(candidate, PosHandheld, StringComparison.OrdinalIgnoreCase))
        {
            normalized = PosHandheld;
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    public static bool TryNormalizeOrLegacyMobile(string? value, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = Mobile;
            return true;
        }

        return TryNormalize(value, out normalized);
    }
}
