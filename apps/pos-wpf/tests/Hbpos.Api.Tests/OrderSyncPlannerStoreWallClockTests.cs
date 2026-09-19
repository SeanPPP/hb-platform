using Hbpos.Api.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Orders;

namespace Hbpos.Api.Tests;

/// <summary>
/// POSM 的时间列存门店本地墙钟时间，这里覆盖写入侧的时区换算，包括两个夏令时切换日。
/// </summary>
public sealed class OrderSyncPlannerStoreWallClockTests
{
    [Theory]
    // 布里斯班全年 UTC+10：客户端带偏移与 iPad 的 Z 偏移应得到同一个墙钟时间。
    [InlineData("2026-09-19T09:30:00+10:00", "Brisbane", "2026-09-19 09:30:00")]
    [InlineData("2026-09-18T23:30:00Z", "Brisbane", "2026-09-19 09:30:00")]
    // 悉尼夏令时 2026-10-04 开始，之后偏移是 UTC+11。
    [InlineData("2026-10-03T23:30:00Z", "Sydney", "2026-10-04 10:30:00")]
    // 悉尼夏令时 2027-04-04 结束，之后偏移回到 UTC+10。
    [InlineData("2027-04-04T23:30:00Z", "Sydney", "2027-04-05 09:30:00")]
    public void CreatePlan_writes_store_wall_clock_order_time(
        string soldAtText,
        string storeTimeZoneName,
        string expectedOrderTimeText)
    {
        var storeTimeZone = storeTimeZoneName == "Brisbane"
            ? TestStoreTimeZones.Brisbane
            : TestStoreTimeZones.Sydney;

        var plan = new OrderSyncPlanner().CreatePlan(
            CreateRequest(DateTimeOffset.Parse(soldAtText)),
            storeTimeZone);

        var expected = DateTime.Parse(expectedOrderTimeText);
        Assert.Equal(expected, plan.Order.OrderTime);
        // 旧 POS 的订单、明细、支付 CreatedTime 都等于下单时刻，新 POS 对齐该口径。
        Assert.Equal(expected, plan.Order.CreatedTime);
        Assert.All(plan.Lines, line => Assert.Equal(expected, line.CreatedTime));
        Assert.All(plan.Payments, payment => Assert.Equal(expected, payment.CreatedTime));
    }

    [Fact]
    public void CreatePlan_writes_upload_time_in_store_wall_clock()
    {
        var soldAt = DateTimeOffset.UtcNow.AddDays(-4);
        var expectedUploadDate = TimeZoneInfo
            .ConvertTime(DateTimeOffset.UtcNow, TestStoreTimeZones.Brisbane)
            .Date;

        var plan = new OrderSyncPlanner().CreatePlan(CreateRequest(soldAt), TestStoreTimeZones.Brisbane);

        // 离线补传：下单时刻保持销售当天，上传时刻是补传当天，两者都按门店时区。
        Assert.Equal(
            TimeZoneInfo.ConvertTime(soldAt, TestStoreTimeZones.Brisbane).DateTime,
            plan.Order.OrderTime);
        Assert.Equal(expectedUploadDate, plan.Order.LastUploadTime!.Value.Date);
        Assert.Equal(expectedUploadDate, plan.Order.UpdatedTime!.Value.Date);
        Assert.All(plan.Lines, line => Assert.Equal(expectedUploadDate, line.LastUploadTime!.Value.Date));
    }

    [Fact]
    public void CreatePlan_writes_bank_date_time_in_store_wall_clock()
    {
        var soldAt = DateTimeOffset.Parse("2026-09-18T23:30:00Z");
        var request = CreateRequest(
            soldAt,
            cardTransaction: new CardTransactionDto(
                "Linkly",
                "TXN-1",
                "AUTH-1",
                "VISA",
                411111,
                "411111******1111",
                "MERCHANT-1",
                "00",
                "APPROVED",
                "123456",
                soldAt,
                9.9m,
                null));

        var plan = new OrderSyncPlanner().CreatePlan(request, TestStoreTimeZones.Brisbane);

        var transaction = Assert.Single(plan.BankTransactions);
        Assert.Equal(DateTime.Parse("2026-09-19 09:30:00"), transaction.BankDateTime);
    }

    [Fact]
    public void CreatePlan_writes_line_count_into_item_count()
    {
        var request = CreateRequest(DateTimeOffset.UtcNow, quantity: 3m);

        var plan = new OrderSyncPlanner().CreatePlan(request, TestStoreTimeZones.Sydney);

        // 旧 POS 的 ItemCount 写的是明细行数，件数由明细 Quantity 求和。
        Assert.Equal(1, plan.Order.ItemCount);
        Assert.Equal(3, Assert.Single(plan.Lines).Quantity);
    }

    private static OrderSyncRequest CreateRequest(
        DateTimeOffset soldAt,
        decimal quantity = 1m,
        CardTransactionDto? cardTransaction = null)
    {
        return new OrderSyncRequest(
            Guid.NewGuid(),
            "1042",
            "POS_1042_0200",
            "C01",
            "Cashier",
            soldAt,
            9.9m,
            0m,
            9.9m,
            [
                new OrderLineSyncDto(
                    Guid.NewGuid(),
                    "P001",
                    null,
                    "Product",
                    "P001",
                    quantity,
                    9.9m,
                    0m,
                    9.9m,
                    PriceSourceKind.StoreRetailPrice)
            ],
            [
                new PaymentSyncDto(
                    Guid.NewGuid(),
                    cardTransaction is null ? PaymentMethodKind.Cash : PaymentMethodKind.Card,
                    9.9m,
                    null,
                    null,
                    cardTransaction is null ? null : [cardTransaction])
            ]);
    }
}
