using Hbpos.Api.Services;

namespace Hbpos.Api.Tests;

/// <summary>
/// 测试用的门店时区解析：固定返回指定时区，不访问数据库。
/// </summary>
public sealed class StubStoreTimeZoneResolver(TimeZoneInfo? timeZone = null) : IStoreTimeZoneResolver
{
    private readonly TimeZoneInfo timeZone = timeZone ?? TestStoreTimeZones.Sydney;

    public Task<TimeZoneInfo> ResolveAsync(string? storeCode, CancellationToken cancellationToken)
    {
        return Task.FromResult(this.timeZone);
    }
}

public static class TestStoreTimeZones
{
    // 悉尼有夏令时，布里斯班全年 UTC+10，两者覆盖了在用门店的全部情况。
    public static readonly TimeZoneInfo Sydney = Find("Australia/Sydney", "AUS Eastern Standard Time");
    public static readonly TimeZoneInfo Brisbane = Find("Australia/Brisbane", "E. Australia Standard Time");

    private static TimeZoneInfo Find(params string[] timeZoneIds)
    {
        foreach (var timeZoneId in timeZoneIds)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }

        throw new TimeZoneNotFoundException(string.Join(", ", timeZoneIds));
    }
}
