using BlazorApp.Api.Services;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesStatisticsBusinessDateTests
{
    [Theory]
    [InlineData("2026-09-06T23:46:08+00:00", "2026-09-07")]
    [InlineData("2026-09-07T13:59:59+00:00", "2026-09-07")]
    [InlineData("2026-09-07T14:00:00+00:00", "2026-09-08")]
    public void GetBusinessDate_跨UTC日期时仍按悉尼业务日计算(string utcText, string expectedDateText)
    {
        var utcNow = DateTimeOffset.Parse(utcText);

        var businessDate = SalesStatisticsBusinessDate.GetBusinessDate(utcNow);

        Assert.Equal(DateTime.Parse(expectedDateText), businessDate);
    }

    [Fact]
    public void GetBusinessNow_悉尼夏令时切换时保留正确偏移并保持业务日期()
    {
        var beforeTransition = SalesStatisticsBusinessDate.GetBusinessNowOffset(
            new DateTimeOffset(2026, 10, 3, 15, 59, 59, TimeSpan.Zero));
        var afterTransition = SalesStatisticsBusinessDate.GetBusinessNowOffset(
            new DateTimeOffset(2026, 10, 3, 16, 0, 0, TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromHours(10), beforeTransition.Offset);
        Assert.Equal(TimeSpan.FromHours(11), afterTransition.Offset);
        Assert.Equal(new DateTime(2026, 10, 4), beforeTransition.Date);
        Assert.Equal(new DateTime(2026, 10, 4), afterTransition.Date);
    }

    [Fact]
    public void GetBusinessDate_历史今日未来边界按同一业务日判断()
    {
        var utcNow = new DateTimeOffset(2026, 9, 6, 23, 46, 8, TimeSpan.Zero);
        var today = SalesStatisticsBusinessDate.GetBusinessDate(utcNow);

        Assert.True(SalesStatisticsBusinessDate.IsHistorical(new DateTime(2026, 9, 6), utcNow));
        Assert.True(SalesStatisticsBusinessDate.IsToday(new DateTime(2026, 9, 7), utcNow));
        Assert.False(SalesStatisticsBusinessDate.IsToday(new DateTime(2026, 9, 8), utcNow));
        Assert.Equal(new DateTime(2026, 9, 7), today);
    }
}
