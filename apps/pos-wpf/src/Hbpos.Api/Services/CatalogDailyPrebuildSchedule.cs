namespace Hbpos.Api.Services;

/// <summary>计算每日目录预构建的下一个固定本地执行点，不对已错过的时点补跑。</summary>
public static class CatalogDailyPrebuildSchedule
{
    private static readonly TimeSpan RunAtLocalTime = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartWindow = TimeSpan.FromMinutes(5);

    public static DateTimeOffset GetNextRunUtc(DateTimeOffset now, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var nextLocal = localNow.Date.Add(RunAtLocalTime);
        if (localNow.TimeOfDay > RunAtLocalTime)
        {
            nextLocal = nextLocal.AddDays(1);
        }

        return new DateTimeOffset(nextLocal, timeZone.GetUtcOffset(nextLocal)).ToUniversalTime();
    }

    public static bool IsWithinStartWindow(
        DateTimeOffset now,
        DateTimeOffset scheduledRunUtc)
    {
        var elapsed = now - scheduledRunUtc;
        return elapsed >= TimeSpan.Zero && elapsed < StartWindow;
    }
}
