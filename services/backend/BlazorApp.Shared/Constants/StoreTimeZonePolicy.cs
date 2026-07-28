namespace BlazorApp.Shared.Constants
{
    /// <summary>
    /// 门店考勤允许配置的 IANA 时区，集中避免各调用方出现不一致的白名单。
    /// </summary>
    public static class StoreTimeZonePolicy
    {
        public const string Brisbane = "Australia/Brisbane";
        public const string Sydney = "Australia/Sydney";
        public const string Melbourne = "Australia/Melbourne";

        public static bool TryNormalize(string? value, out string? timeZoneId)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                timeZoneId = null;
                return true;
            }

            if (string.Equals(normalized, Brisbane, StringComparison.OrdinalIgnoreCase))
            {
                timeZoneId = Brisbane;
                return true;
            }

            if (string.Equals(normalized, Sydney, StringComparison.OrdinalIgnoreCase))
            {
                timeZoneId = Sydney;
                return true;
            }

            if (string.Equals(normalized, Melbourne, StringComparison.OrdinalIgnoreCase))
            {
                timeZoneId = Melbourne;
                return true;
            }

            timeZoneId = null;
            return false;
        }
    }
}
