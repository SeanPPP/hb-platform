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

    [Theory]
    [InlineData("2026-09-07T11:59:59+00:00", false)]
    [InlineData("2026-09-07T12:00:00+00:00", true)]
    [InlineData("2026-09-07T13:59:59+00:00", true)]
    [InlineData("2026-09-07T14:00:00+00:00", true)]
    [InlineData("2026-09-07T19:59:59+00:00", true)]
    [InlineData("2026-09-07T20:00:00+00:00", false)]
    [InlineData("2026-10-04T10:59:59+00:00", false)]
    [InlineData("2026-10-04T11:00:00+00:00", true)]
    [InlineData("2026-10-04T12:59:59+00:00", true)]
    [InlineData("2026-10-04T13:00:00+00:00", true)]
    [InlineData("2026-10-04T18:59:59+00:00", true)]
    [InlineData("2026-10-04T19:00:00+00:00", false)]
    public void HistoricalRefreshWindow_按悉尼本地二十二点至六点判断并支持TimeProvider(
        string utcText,
        bool expectedOpen)
    {
        var clock = new FixedTimeProvider(DateTimeOffset.Parse(utcText));

        Assert.Equal(expectedOpen, SalesStatisticsHistoricalRefreshWindow.IsOpen(clock));
    }

    [Fact]
    public void HistoricalRefreshWindow_从五点五十九到六点的两次检查停止下一历史日()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 7, 19, 59, 59, TimeSpan.Zero));

        Assert.True(SalesStatisticsHistoricalRefreshWindow.IsOpen(clock));

        clock.UtcNow = new DateTimeOffset(2026, 9, 7, 20, 0, 0, TimeSpan.Zero);

        Assert.False(SalesStatisticsHistoricalRefreshWindow.IsOpen(clock));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
