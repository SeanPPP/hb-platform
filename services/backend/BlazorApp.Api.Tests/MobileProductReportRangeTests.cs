using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>
/// 预热生成的日期范围必须与移动端 periods.ts 的日范围规则逐字节一致，否则缓存键对不上。
/// 期望值取自 App 真实请求（2026-09-22 → 同期 2025-09-23）与 ISO 8601 定义。
/// </summary>
public sealed class MobileProductReportRangeTests
{
    [Theory]
    [InlineData("2026-09-22", "2025-09-23")] // 周二：2026-W39-2 → 2025-W39-2
    [InlineData("2026-09-21", "2025-09-22")] // 周一
    [InlineData("2026-09-27", "2025-09-28")] // 周日
    [InlineData("2026-01-01", "2025-01-02")] // 2026-01-01 属 2026-W01-4，2025-W01-4 = 2025-01-02
    [InlineData("2020-12-31", "2019-12-26")] // 2020 有 53 周（W53-4），2019 只有 52 周 → 取 2019-W52-4
    public void 日范围同期取去年同ISO周同星期几(string date, string expectedCompare)
    {
        var range = MobileProductReportRange.Day(DateTime.Parse(date));

        Assert.Equal(DateTime.Parse(date), range.StartDate);
        Assert.Equal(DateTime.Parse(date), range.EndDate);
        Assert.Equal(DateTime.Parse(expectedCompare), range.CompareStartDate);
        Assert.Equal(DateTime.Parse(expectedCompare), range.CompareEndDate);
        Assert.Equal(CompareMode.ByWeek, range.CompareMode);
    }

    [Fact]
    public void 范围字符串与控制器按日期参数构造的一致()
    {
        // 缓存键只看 DateRangeDto.ToString()；控制器从 yyyy-MM-dd 查询串构造的 DateTime 没有时间部分。
        var range = MobileProductReportRange.Day(new DateTime(2026, 9, 22, 13, 45, 0));
        Assert.Equal("2026-09-22|2026-09-22|2025-09-23|2025-09-23|ByWeek", range.ToString());
    }
}
