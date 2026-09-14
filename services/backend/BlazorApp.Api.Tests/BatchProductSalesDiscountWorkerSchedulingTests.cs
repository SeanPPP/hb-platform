using BlazorApp.Api.Services.Background;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class BatchProductSalesDiscountWorkerSchedulingTests
{
    [Fact]
    public void BuildCoverageDays_精确覆盖当天向前两年并保留闰日差异()
    {
        var today = new DateTime(2028, 2, 29);

        var days = BatchProductSalesDiscountWorker.BuildCoverageDays(today, 2);

        Assert.Equal(new DateTime(2026, 2, 28), days.First());
        Assert.Equal(today, days.Last());
        Assert.Equal((today - new DateTime(2026, 2, 28)).Days + 1, days.Count);
    }

    [Fact]
    public void OrderEligibleCandidates_最近日期到期时优先刷新_未到期时历史继续回填()
    {
        var now = new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);
        var today = now.Date;
        var historical = new BatchProductSalesDiscountRefreshState
        {
            Date = today.AddDays(-30), Status = "Queued", NextAttemptAtUtc = now,
        };
        var recent = new BatchProductSalesDiscountRefreshState
        {
            Date = today, Status = "Fresh", LastCheckedAtUtc = now.AddMinutes(-5),
        };
        var preferred = new HashSet<DateTime> { today };

        var withDueRecent = BatchProductSalesDiscountDailyStore.OrderEligibleCandidates(
            [historical, recent], now, preferred, today.AddYears(-2), today, TimeSpan.FromMinutes(5));

        Assert.Equal(today, withDueRecent.First().Date);

        recent.LastCheckedAtUtc = now;
        var afterRecentCheck = BatchProductSalesDiscountDailyStore.OrderEligibleCandidates(
            [historical, recent], now, preferred, today.AddYears(-2), today, TimeSpan.FromMinutes(5));

        Assert.Single(afterRecentCheck);
        Assert.Equal(historical.Date, afterRecentCheck[0].Date);
    }

    [Fact]
    public void OrderEligibleCandidates_非最近历史回填恢复优先于较新的Fresh巡检并按日期从近到远()
    {
        var now = new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);
        var oldQueued = new BatchProductSalesDiscountRefreshState
        {
            Date = now.Date.AddDays(-90), Status = "Queued", NextAttemptAtUtc = now,
        };
        var newerFresh = new BatchProductSalesDiscountRefreshState
        {
            Date = now.Date.AddDays(-4), Status = "Fresh", LastCheckedAtUtc = now.AddDays(-1),
        };
        var newerQueued = new BatchProductSalesDiscountRefreshState
        {
            Date = now.Date.AddDays(-30), Status = "Queued", NextAttemptAtUtc = now,
        };

        var ordered = BatchProductSalesDiscountDailyStore.OrderEligibleCandidates(
            [newerFresh, oldQueued, newerQueued], now, new HashSet<DateTime>(), now.Date.AddYears(-2), now.Date,
            TimeSpan.FromMinutes(5));

        Assert.Equal([newerQueued.Date, oldQueued.Date, newerFresh.Date], ordered.Select(state => state.Date));
    }

    [Fact]
    public void OrderEligibleCandidates_历史Fresh按最早巡检时间而非日期排序()
    {
        var now = new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);
        var olderDateRecentlyChecked = new BatchProductSalesDiscountRefreshState
        {
            Date = now.Date.AddDays(-90), Status = "Fresh", LastCheckedAtUtc = now.AddDays(-2),
        };
        var newerDateLongestUnchecked = new BatchProductSalesDiscountRefreshState
        {
            Date = now.Date.AddDays(-4), Status = "Fresh", LastCheckedAtUtc = now.AddDays(-3),
        };

        var ordered = BatchProductSalesDiscountDailyStore.OrderEligibleCandidates(
            [olderDateRecentlyChecked, newerDateLongestUnchecked], now, new HashSet<DateTime>(),
            now.Date.AddYears(-2), now.Date, TimeSpan.FromMinutes(5));

        Assert.Equal([newerDateLongestUnchecked.Date, olderDateRecentlyChecked.Date], ordered.Select(state => state.Date));
    }

    [Fact]
    public void Canonical等待不消耗失败次数_仅明确失败允许重新入队_范围外日期不调度()
    {
        var now = new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);
        var inRange = new BatchProductSalesDiscountRefreshState
        {
            Date = now.Date.AddDays(-2), Status = BatchProductSalesDiscountDailyStore.WaitingForCanonicalStatus,
            NextAttemptAtUtc = now,
        };
        var outsideRange = new BatchProductSalesDiscountRefreshState
        {
            Date = now.Date.AddYears(-3), Status = "Queued", NextAttemptAtUtc = now,
        };

        var eligible = BatchProductSalesDiscountDailyStore.OrderEligibleCandidates(
            [inRange, outsideRange], now, new HashSet<DateTime>(), now.Date.AddYears(-2), now.Date, TimeSpan.FromMinutes(5));

        Assert.False(BatchProductSalesDiscountDailyStore.ShouldConsumeFailureAttempt(
            BatchProductSalesDiscountDailyStore.WaitingForCanonicalStatus));
        Assert.True(BatchProductSalesDiscountDailyStore.ShouldConsumeFailureAttempt("Failed"));
        Assert.Equal(0, BatchProductSalesDiscountDailyStore.AttemptsAfterCanonicalWait(1, "Queued"));
        Assert.Equal(2, BatchProductSalesDiscountDailyStore.AttemptsAfterCanonicalWait(3, "Failed"));
        Assert.Equal(2, BatchProductSalesDiscountDailyStore.AttemptsAfterCanonicalWait(
            2, BatchProductSalesDiscountDailyStore.WaitingForCanonicalStatus));
        Assert.False(BatchProductSalesDiscountDailyStore.ShouldRequestCanonicalReconciliation(true, "Queued"));
        Assert.True(BatchProductSalesDiscountDailyStore.ShouldRequestCanonicalReconciliation(true, "Failed"));
        Assert.True(BatchProductSalesDiscountDailyStore.ShouldRequestCanonicalReconciliation(true, null));
        Assert.Single(eligible);
        Assert.Equal(inRange.Date, eligible[0].Date);
    }

    [Fact]
    public void BuildPayload_聚合不一致诊断前五个商品门店并保留严格四位金额口径()
    {
        var day = new DateTime(2026, 9, 14);
        var statistics = Enumerable.Range(1, 6).Select(index => new BatchProductSalesAggregateRow
        {
            Date = day, ProductCode = "P1", BranchCode = $"S{index}", Quantity = 2m, SalesAmount = 20m,
        }).Append(new BatchProductSalesAggregateRow
        {
            Date = day, ProductCode = "P2", BranchCode = "MATCH", Quantity = 1m, SalesAmount = 10m,
        }).ToList();
        var source = Enumerable.Range(1, 6).Select(index => new BatchProductSalesAggregateRow
        {
            Date = day, ProductCode = "P1", BranchCode = $"S{index}", Quantity = 1m, SalesAmount = 19.9999m,
        }).Append(new BatchProductSalesAggregateRow
        {
            // 10.00004 按现有 TotalsMatch 的四位存储精度仍与 10 相等，不应进入诊断。
            Date = day, ProductCode = "P2", BranchCode = "MATCH", Quantity = 1m, SalesAmount = 10.00004m,
        }).ToList();

        var payload = BatchProductSalesDiscountWorker.BuildPayload(day, statistics, source, out var mismatches);

        Assert.Equal(["P2"], payload.Keys);
        Assert.Equal(5, mismatches.Count);
        Assert.Equal(["S1", "S2", "S3", "S4", "S5"], mismatches.Select(row => row.StoreCode));
        Assert.All(mismatches, mismatch =>
        {
            Assert.Equal("P1", mismatch.ProductCode);
            Assert.Equal(2m, mismatch.ExpectedQuantity);
            Assert.Equal(1m, mismatch.SourceQuantity);
            Assert.Equal(20m, mismatch.ExpectedAmount);
            Assert.Equal(19.9999m, mismatch.SourceAmount);
        });
    }
}
