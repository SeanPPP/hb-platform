using BlazorApp.Shared.Constants;

namespace BlazorApp.Api.Services;

/// <summary>
/// 销售统计统一使用悉尼业务时区，避免容器 UTC 日期把当天统计错算为前一天。
/// </summary>
internal static class SalesStatisticsBusinessDate
{
    private static readonly TimeZoneInfo SydneyTimeZone = ResolveSydneyTimeZone();

    internal static DateTime Now() => GetBusinessNow(DateTimeOffset.UtcNow);

    internal static DateTime Today() => Now().Date;

    internal static DateTime GetBusinessNow(DateTimeOffset utcNow) =>
        GetBusinessNowOffset(utcNow).DateTime;

    internal static DateTimeOffset GetBusinessNowOffset(DateTimeOffset utcNow) =>
        TimeZoneInfo.ConvertTime(utcNow, SydneyTimeZone);

    internal static DateTime GetBusinessDate(DateTimeOffset utcNow) =>
        GetBusinessNow(utcNow).Date;

    internal static bool IsToday(DateTime targetDate) => targetDate.Date == Today();

    internal static bool IsHistorical(DateTime targetDate) => targetDate.Date < Today();

    internal static bool IsToday(DateTime targetDate, DateTimeOffset utcNow) =>
        targetDate.Date == GetBusinessDate(utcNow);

    internal static bool IsHistorical(DateTime targetDate, DateTimeOffset utcNow) =>
        targetDate.Date < GetBusinessDate(utcNow);

    private static TimeZoneInfo ResolveSydneyTimeZone()
    {
        foreach (var id in new[] { StoreTimeZonePolicy.Sydney, "AUS Eastern Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        throw new TimeZoneNotFoundException("找不到 Australia/Sydney 业务时区");
    }
}
