using BlazorApp.Api.Features.ProductInsights;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class StoreProductInsightRulesTests
{
    [Fact]
    public void CanAccessStore_限制登录用户和设备不能跨店读取()
    {
        Assert.True(StoreProductInsightRules.CanAccessStore(null, "S001"));
        Assert.True(StoreProductInsightRules.CanAccessStore(["S001", "S002"], "S002"));
        Assert.False(StoreProductInsightRules.CanAccessStore(["S001"], "S002"));
    }

    [Fact]
    public void IsValidRange_拒绝反向日期边界()
    {
        Assert.True(StoreProductInsightRules.IsValidRange(new DateTime(2026, 9, 14), new DateTime(2026, 9, 14)));
        Assert.False(StoreProductInsightRules.IsValidRange(new DateTime(2026, 9, 15), new DateTime(2026, 9, 14)));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void IsValidLocalInbound_只接受已完成入库的准确数量口径(int inboundStatus, bool expected)
    {
        var actual = StoreProductInsightRules.IsValidLocalInbound(
            isInvoiceDeleted: false,
            isDetailDeleted: false,
            inboundDate: inboundStatus == 0 ? null : new DateTime(2026, 9, 14),
            inboundStatus: inboundStatus
        );

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsWarehouseDelivery_按出库日含首尾且排除未完成配货()
    {
        var start = new DateTime(2026, 6, 17);
        var end = new DateTime(2026, 9, 14);

        Assert.True(StoreProductInsightRules.IsWarehouseDelivery(2, start, 3, start, end));
        Assert.True(StoreProductInsightRules.IsWarehouseDelivery(2, end.AddHours(23), 3, start, end));
        Assert.False(StoreProductInsightRules.IsWarehouseDelivery(3, end, 3, start, end));
        Assert.False(StoreProductInsightRules.IsWarehouseDelivery(2, end, 0, start, end));
        Assert.False(StoreProductInsightRules.IsWarehouseDelivery(2, end.AddDays(1), 3, start, end));
    }

    [Fact]
    public void IsHistoricalRecordEligible_不让最近历史穿过查询结束日()
    {
        var end = new DateTime(2026, 9, 14);

        Assert.True(StoreProductInsightRules.IsHistoricalRecordEligible(end, end));
        Assert.False(StoreProductInsightRules.IsHistoricalRecordEligible(end.AddDays(1), end));
    }
}
