using BlazorApp.Api.Services.Background;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductStoreDailyColumnstoreMaintenancePolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void 已删除行超过两成或小行组过多时才整理()
    {
        var healthy = new ProductStoreDailyColumnstoreHealth(RowGroups: 8, SmallRowGroups: 1, TotalRows: 7_500_000, DeletedRows: 100_000);
        Assert.False(ProductStoreDailyColumnstoreMaintenancePolicy.ShouldReorganize(healthy, null, Now, TimeSpan.FromHours(4)));

        // 2026-09-22 生产实测：30.3M 行中 21.9M 已删除、5400 个行组。
        var bloated = new ProductStoreDailyColumnstoreHealth(5400, 5300, 30_273_413, 21_876_607);
        Assert.True(ProductStoreDailyColumnstoreMaintenancePolicy.ShouldReorganize(bloated, null, Now, TimeSpan.FromHours(4)));

        var fragmented = new ProductStoreDailyColumnstoreHealth(80, 70, 7_500_000, 0);
        Assert.True(ProductStoreDailyColumnstoreMaintenancePolicy.ShouldReorganize(fragmented, null, Now, TimeSpan.FromHours(4)));

        var empty = new ProductStoreDailyColumnstoreHealth(0, 0, 0, 0);
        Assert.False(ProductStoreDailyColumnstoreMaintenancePolicy.ShouldReorganize(empty, null, Now, TimeSpan.FromHours(4)));
    }

    [Fact]
    public void 最小间隔内不重复整理()
    {
        var bloated = new ProductStoreDailyColumnstoreHealth(5400, 5300, 30_273_413, 21_876_607);
        Assert.False(ProductStoreDailyColumnstoreMaintenancePolicy.ShouldReorganize(bloated, Now.AddHours(-1), Now, TimeSpan.FromHours(4)));
        Assert.True(ProductStoreDailyColumnstoreMaintenancePolicy.ShouldReorganize(bloated, Now.AddHours(-5), Now, TimeSpan.FromHours(4)));
    }

    [Fact]
    public void 整理语句针对固定索引且在线执行()
    {
        Assert.Equal(
            "ALTER INDEX [IX_LSPSA_Sales_Analytics] ON [dbo].[ProductStoreDailySalesStatistic] REORGANIZE WITH (COMPRESS_ALL_ROW_GROUPS = ON);",
            ProductStoreDailyColumnstoreMaintenancePolicy.ReorganizeSql);
        Assert.Contains("sys.column_store_row_groups", ProductStoreDailyColumnstoreMaintenancePolicy.HealthSql);
        Assert.DoesNotContain("dm_db_column_store_row_group_physical_stats", ProductStoreDailyColumnstoreMaintenancePolicy.HealthSql);
    }
}
