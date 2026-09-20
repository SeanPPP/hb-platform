using BlazorApp.Api.Services;
using Xunit;

namespace BlazorApp.Api.Tests;

public class SalesStatisticsHBSalesHistoryWindowTests
{
    [Theory]
    [InlineData(2024, 9, 13, false)]
    [InlineData(2024, 9, 14, true)]
    [InlineData(2025, 9, 18, true)]
    // 2025-10 起逐店切换到 POSM，并存期仍在窗口内；HBSales 最后一笔结账为 2026-04-12。
    [InlineData(2025, 12, 31, true)]
    [InlineData(2026, 4, 30, true)]
    [InlineData(2026, 5, 1, false)]
    public void Includes_按已核验的HBSales历史窗口判定(int year, int month, int day, bool expected)
    {
        Assert.Equal(expected, SalesStatisticsHBSalesHistoryWindow.Includes(new DateTime(year, month, day, 15, 30, 0)));
    }
}
