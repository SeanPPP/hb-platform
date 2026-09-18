using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class LocalSupplierPurchaseSalesDailySeriesBuilderTests
{
    [Fact]
    public void BuildDailySeries_缺失日期补零且首尾两天都包含()
    {
        var start = new DateTime(2026, 9, 1);
        var end = new DateTime(2026, 9, 5);
        var quantities = new Dictionary<DateTime, int>
        {
            [new DateTime(2026, 9, 1)] = 3,
            [new DateTime(2026, 9, 5)] = 7,
        };

        var series = LocalSupplierPurchaseSalesDailySeriesBuilder.BuildDailySeries(
            start,
            end,
            quantities
        );

        Assert.Equal(5, series.Count);
        Assert.Equal(
            new[] { start, start.AddDays(1), start.AddDays(2), start.AddDays(3), end },
            series.Select(point => point.Date)
        );
        Assert.Equal(new[] { 3, 0, 0, 0, 7 }, series.Select(point => point.Quantity));
    }

    [Fact]
    public void BuildDailySeries_退货负数原样保留()
    {
        var day = new DateTime(2026, 9, 2);
        var series = LocalSupplierPurchaseSalesDailySeriesBuilder.BuildDailySeries(
            day.AddDays(-1),
            day,
            new Dictionary<DateTime, int> { [day] = -4 }
        );

        Assert.Equal(new[] { 0, -4 }, series.Select(point => point.Quantity));
    }

    [Fact]
    public void BuildDailySeries_忽略时间部分且窗口外的数据不进入序列()
    {
        var series = LocalSupplierPurchaseSalesDailySeriesBuilder.BuildDailySeries(
            new DateTime(2026, 9, 1, 15, 30, 0),
            new DateTime(2026, 9, 2, 8, 0, 0),
            new Dictionary<DateTime, int>
            {
                [new DateTime(2026, 8, 31)] = 99,
                [new DateTime(2026, 9, 2)] = 2,
                [new DateTime(2026, 9, 3)] = 99,
            }
        );

        Assert.Equal(
            new[] { new DateTime(2026, 9, 1), new DateTime(2026, 9, 2) },
            series.Select(point => point.Date)
        );
        Assert.Equal(new[] { 0, 2 }, series.Select(point => point.Quantity));
    }

    [Fact]
    public void BuildDailySeries_同一天返回单点()
    {
        var day = new DateTime(2026, 9, 18);

        var series = LocalSupplierPurchaseSalesDailySeriesBuilder.BuildDailySeries(
            day,
            day,
            new Dictionary<DateTime, int>()
        );

        var point = Assert.Single(series);
        Assert.Equal(day, point.Date);
        Assert.Equal(0, point.Quantity);
    }

    [Fact]
    public void BuildDailySeries_开始晚于结束时约定返回空列表()
    {
        // 约定：BuildDailySeries 本身不修正窗口，start > end 直接返回空；窗口修正由 ResolveWindow 负责。
        var series = LocalSupplierPurchaseSalesDailySeriesBuilder.BuildDailySeries(
            new DateTime(2026, 9, 10),
            new DateTime(2026, 9, 9),
            new Dictionary<DateTime, int> { [new DateTime(2026, 9, 10)] = 5 }
        );

        Assert.Empty(series);
    }

    [Fact]
    public void ResolveWindow_有上次进货时从上次进货日到今天()
    {
        var (start, end) = LocalSupplierPurchaseSalesDailySeriesBuilder.ResolveWindow(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 9, 1),
            new DateTime(2026, 9, 18)
        );

        Assert.Equal(new DateTime(2026, 7, 1), start);
        Assert.Equal(new DateTime(2026, 9, 18), end);
    }

    [Fact]
    public void ResolveWindow_没有上次进货时回看最近进货前30天()
    {
        var (start, end) = LocalSupplierPurchaseSalesDailySeriesBuilder.ResolveWindow(
            null,
            new DateTime(2026, 9, 1),
            new DateTime(2026, 9, 18)
        );

        Assert.Equal(new DateTime(2026, 8, 2), start);
        Assert.Equal(new DateTime(2026, 9, 18), end);
    }

    [Fact]
    public void ResolveWindow_结束不得早于开始_未来进货日收敛为单点窗口()
    {
        var (start, end) = LocalSupplierPurchaseSalesDailySeriesBuilder.ResolveWindow(
            new DateTime(2026, 10, 1),
            new DateTime(2026, 10, 5),
            new DateTime(2026, 9, 18)
        );

        Assert.Equal(new DateTime(2026, 10, 1), start);
        Assert.Equal(start, end);
        Assert.Single(
            LocalSupplierPurchaseSalesDailySeriesBuilder.BuildDailySeries(
                start,
                end,
                new Dictionary<DateTime, int>()
            )
        );
    }

    [Fact]
    public void BuildPurchaseEvents_上次与最近进货都加入且数量远离零取整()
    {
        var events = LocalSupplierPurchaseSalesDailySeriesBuilder.BuildPurchaseEvents(
            new DateTime(2026, 7, 1),
            12.5m,
            new DateTime(2026, 9, 1),
            23.4m
        );

        Assert.Equal(2, events.Count);
        Assert.Equal(new DateTime(2026, 7, 1), events[0].Date);
        Assert.Equal(13, events[0].Quantity);
        Assert.Equal(new DateTime(2026, 9, 1), events[1].Date);
        Assert.Equal(23, events[1].Quantity);
    }

    [Fact]
    public void BuildPurchaseEvents_上次进货缺日期或数量时只保留最近进货()
    {
        var withoutQty = LocalSupplierPurchaseSalesDailySeriesBuilder.BuildPurchaseEvents(
            new DateTime(2026, 7, 1),
            null,
            new DateTime(2026, 9, 1),
            6m
        );
        var withoutDate = LocalSupplierPurchaseSalesDailySeriesBuilder.BuildPurchaseEvents(
            null,
            5m,
            new DateTime(2026, 9, 1),
            6m
        );

        Assert.Equal(new DateTime(2026, 9, 1), Assert.Single(withoutQty).Date);
        Assert.Equal(6, Assert.Single(withoutDate).Quantity);
    }

    [Fact]
    public void BuildPurchaseEvents_没有最近进货日期时返回空列表()
    {
        Assert.Empty(
            LocalSupplierPurchaseSalesDailySeriesBuilder.BuildPurchaseEvents(
                new DateTime(2026, 7, 1),
                5m,
                null,
                6m
            )
        );
    }

    [Fact]
    public void RowDto_新增的列表属性不影响原生SQL行映射()
    {
        // 分页 SQL 行类型继承自 RowDto：确认 SqlSugar 映射时会忽略结果集中不存在的 List 属性，而不是抛错。
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = connection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
            }
        );

        var row = Assert.Single(
            db.Ado.SqlQuery<LocalSupplierPurchaseSalesAnalysisRowDto>(
                "SELECT 'S1' AS StoreCode, 'P1' AS ProductCode, 5 AS SalesQty30"
            )
        );

        Assert.Equal("S1", row.StoreCode);
        Assert.Equal("P1", row.ProductCode);
        Assert.Equal(5, row.SalesQty30);
        Assert.Empty(row.DailySales);
        Assert.Empty(row.Purchases);
    }

    [Fact]
    public async Task AttachDailySeries_一次查询按商品和日期求和且只取本门店窗口内数据()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = CreateSqliteClient(connection);
        db.CodeFirst.InitTables<ProductStoreDailySalesStatistic>();
        var today = new DateTime(2026, 9, 5);
        db.Insertable(
            new List<ProductStoreDailySalesStatistic>
            {
                // 同一商品同一天两条不同供应商记录需要求和；退货负数保留。
                CreateStatistic(new DateTime(2026, 9, 2), "S1", "SUP-A", "P1", 5),
                CreateStatistic(new DateTime(2026, 9, 2), "S1", "SUP-B", "P1", -2),
                CreateStatistic(new DateTime(2026, 9, 5), "S1", "SUP-A", "P1", 4),
                // 其他门店、窗口之前的数据都不能计入。
                CreateStatistic(new DateTime(2026, 9, 2), "S2", "SUP-A", "P1", 100),
                CreateStatistic(new DateTime(2026, 8, 31), "S1", "SUP-A", "P1", 100),
                CreateStatistic(new DateTime(2026, 9, 3), "S1", "SUP-A", "P2", 9),
            }
        ).ExecuteCommand();
        var items = new List<LocalSupplierPurchaseSalesAnalysisRowDto>
        {
            new()
            {
                StoreCode = "S1",
                ProductCode = "P1",
                PreviousPurchaseDate = new DateTime(2026, 9, 1),
                PreviousPurchaseQty = 10m,
                LatestPurchaseDate = new DateTime(2026, 9, 4),
                LatestPurchaseQty = 12m,
            },
            new()
            {
                StoreCode = "S1",
                ProductCode = "P2",
                LatestPurchaseDate = new DateTime(2026, 9, 4),
                LatestPurchaseQty = 6m,
            },
            // 没有最近进货日期：两个列表都留空。
            new() { StoreCode = "S1", ProductCode = "P3" },
        };

        await CreateService(db).AttachDailySeriesAsync(items, today);

        Assert.Equal(new[] { 0, 3, 0, 0, 4 }, items[0].DailySales.Select(point => point.Quantity));
        Assert.Equal(new DateTime(2026, 9, 1), items[0].DailySales[0].Date);
        Assert.Equal(today, items[0].DailySales[^1].Date);
        Assert.Equal(new[] { 10, 12 }, items[0].Purchases.Select(purchase => purchase.Quantity));

        // 无上次进货：窗口从最近进货前 30 天开始，共 32 天。
        Assert.Equal(32, items[1].DailySales.Count);
        Assert.Equal(new DateTime(2026, 8, 5), items[1].DailySales[0].Date);
        Assert.Equal(9, items[1].DailySales.Sum(point => point.Quantity));
        Assert.Equal(6, Assert.Single(items[1].Purchases).Quantity);

        Assert.Empty(items[2].DailySales);
        Assert.Empty(items[2].Purchases);
    }

    [Fact]
    public async Task AttachDailySeries_逐日查询失败时降级为空列表且不抛异常()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        // 故意不建统计表，模拟逐日查询失败。
        using var db = CreateSqliteClient(connection);
        var items = new List<LocalSupplierPurchaseSalesAnalysisRowDto>
        {
            new()
            {
                StoreCode = "S1",
                ProductCode = "P1",
                LatestPurchaseDate = new DateTime(2026, 9, 4),
                LatestPurchaseQty = 12m,
            },
        };

        await CreateService(db).AttachDailySeriesAsync(items, new DateTime(2026, 9, 5));

        Assert.Empty(items[0].DailySales);
        // 进货事件不依赖逐日查询，仍然保留。
        Assert.Single(items[0].Purchases);
    }

    private static SqlSugarClient CreateSqliteClient(SqliteConnection connection) =>
        new(
            new ConnectionConfig
            {
                ConnectionString = connection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );

    private static LocalSupplierInvoiceSalesAnalysisService CreateService(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return new LocalSupplierInvoiceSalesAnalysisService(
            context,
            NullLogger<LocalSupplierInvoiceSalesAnalysisService>.Instance
        );
    }

    private static ProductStoreDailySalesStatistic CreateStatistic(
        DateTime date,
        string branchCode,
        string supplierCode,
        string productCode,
        int quantity
    ) =>
        new()
        {
            Date = date,
            BranchCode = branchCode,
            SupplierCode = supplierCode,
            ProductCode = productCode,
            TotalQuantity = quantity,
        };
}
