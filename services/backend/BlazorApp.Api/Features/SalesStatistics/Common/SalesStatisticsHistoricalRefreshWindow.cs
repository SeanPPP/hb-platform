namespace BlazorApp.Api.Services;

/// <summary>沿用每日 23 点调度，自动历史滚动补算只在悉尼业务时区夜间执行，包含夏令时切换。</summary>
internal static class SalesStatisticsHistoricalRefreshWindow
{
    internal static bool IsOpen(TimeProvider timeProvider) => IsOpen(timeProvider.GetUtcNow());

    internal static bool IsOpen(DateTimeOffset utcNow)
    {
        var localTime = SalesStatisticsBusinessDate.GetBusinessNowOffset(utcNow).TimeOfDay;
        return localTime >= TimeSpan.FromHours(22) || localTime < TimeSpan.FromHours(6);
    }
}
