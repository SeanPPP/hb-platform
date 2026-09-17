using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class BatchProductSalesAnalysisLogicTests
{
    [Fact]
    public void NormalizeItemNumbers_去重但保留前导零()
    {
        var items = BatchProductSalesAnalysisService.NormalizeItemNumbers([" 0012 ", "0012", "12"]);
        Assert.Equal(["0012", "12"], items);
    }

    [Fact]
    public void ResolveStoreScope_受限用户篡改分店被拒绝()
    {
        Assert.Throws<BatchProductSalesAnalysisForbiddenException>(() =>
            BatchProductSalesAnalysisService.ResolveStoreScope(["S2"], ["S1"]));
    }

    [Fact]
    public void ResolveStoreScope_受限用户省略门店只使用可见分店()
    {
        Assert.Equal(["S1"], BatchProductSalesAnalysisService.ResolveStoreScope(null, ["S1"]));
    }

    [Fact]
    public void ValidateRange_未来布里斯班日期被拒绝()
    {
        var tomorrow = DateTime.UtcNow.Date.AddDays(2);
        Assert.Throws<BatchProductSalesAnalysisValidationException>(() =>
            BatchProductSalesAnalysisService.ValidateRange(new BlazorApp.Shared.DTOs.BatchProductSalesScopeDto
            {
                StartDate = tomorrow,
                EndDate = tomorrow,
            }));
    }

    [Fact]
    public void BuildAggregateMetrics_同日正价折扣退货保持有符号守恒()
    {
        var metrics = BatchProductSalesAnalysisService.BuildAggregateMetrics(
        [
            new BatchProductSalesAggregateRow { RegularQuantity = 3m, SalesAmount = 30m, OriginalPriceMax = 10m },
            new BatchProductSalesAggregateRow { DiscountQuantity = 1m, ReturnQuantity = 1m, SalesAmount = 8m, OriginalPriceMin = 10m, DiscountPriceMin = 8m },
        ]);

        Assert.Equal(4m, metrics.Quantity);
        Assert.Equal(3m, metrics.RegularQuantity);
        Assert.Equal(1m, metrics.DiscountQuantity);
        Assert.Equal(0m, metrics.UnknownQuantity);
        Assert.Equal(1m, metrics.ReturnQuantity);
        Assert.Equal(metrics.Quantity, metrics.RegularQuantity + metrics.DiscountQuantity + metrics.UnknownQuantity);
        Assert.Equal("complete", metrics.DiscountStatus);
        Assert.Equal(8m, metrics.DiscountPriceMin);
        Assert.Equal(10m, metrics.OriginalPriceMax);
    }

    [Fact]
    public void BuildAggregateMetrics_历史折扣证据缺失明确为unknown而非正价()
    {
        var metrics = BatchProductSalesAnalysisService.BuildAggregateMetrics(
        [new BatchProductSalesAggregateRow { UnknownQuantity = 2m, SalesAmount = 15m, UnknownRowCount = 1 }]);

        Assert.Equal(0m, metrics.RegularQuantity);
        Assert.Equal(0m, metrics.DiscountQuantity);
        Assert.Equal(2m, metrics.UnknownQuantity);
        Assert.Equal("unknown", metrics.DiscountStatus);
        Assert.Null(metrics.OriginalPriceMin);
        Assert.Null(metrics.DiscountPriceMax);
    }

    [Theory]
    [InlineData(0, "unknown")]
    [InlineData(1, "partial")]
    public void BuildAggregateMetrics_未知净量抵消仍不伪装complete(int knownRows, string expectedStatus)
    {
        var facts = new List<BatchProductSalesAggregateRow>
        {
            new() { UnknownQuantity = 1m, SalesAmount = 10m, UnknownRowCount = 1 },
        };
        if (knownRows == 1)
            facts.Add(new() { DiscountQuantity = 1m, SalesAmount = 8m });

        Assert.Equal(expectedStatus, BatchProductSalesAnalysisService.BuildAggregateMetrics(facts).DiscountStatus);
    }

    [Fact]
    public void SplitStatisticQueueSegments_2025走年度队列其他年份保持31天上限且不含Fresh日期()
    {
        var regularDates = Enumerable.Range(0, 32).Select(offset => new DateTime(2024, 11, 1).AddDays(offset));
        var segments = BatchProductSalesAnalysisService.SplitStatisticQueueSegments(
            regularDates.Concat([new DateTime(2025, 1, 2), new DateTime(2025, 1, 3)]));

        Assert.Equal(3, segments.Count);
        Assert.Equal([31, 1, 2], segments.Select(segment => segment.Dates.Count));
        Assert.Equal([false, false, true], segments.Select(segment => segment.IsYearBackfill));
        Assert.DoesNotContain(segments.SelectMany(segment => segment.Dates), date => date == new DateTime(2025, 1, 1));
    }

    [Fact]
    public async Task QueryAsync_缺失和非Fresh日期按分段提交持久队列且不重建Fresh日期()
    {
        var path = Path.Combine(Path.GetTempPath(), $"batch-product-sales-{Guid.NewGuid():N}.db");
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        try
        {
            db.CodeFirst.InitTables(typeof(Product), typeof(Store), typeof(SalesStatisticRefreshState), typeof(ProductStoreDailySalesStatistic));
            await db.Insertable(new Product { ProductCode = "P1", ItemNumber = "001", ProductName = "测试商品", IsDeleted = false }).ExecuteCommandAsync();
            await db.Insertable(new Store { StoreCode = "S1", StoreName = "测试分店", IsDeleted = false }).ExecuteCommandAsync();
            await db.Insertable(new[]
            {
                new SalesStatisticRefreshState { StatisticType = SalesStatisticType.ProductStoreDaily, Date = new DateTime(2024, 12, 31), Status = SalesStatisticRefreshStatus.Fresh },
                new SalesStatisticRefreshState { StatisticType = SalesStatisticType.ProductStoreDaily, Date = new DateTime(2025, 1, 1), Status = SalesStatisticRefreshStatus.Pending },
            }).ExecuteCommandAsync();
            var queue = new Mock<IProductStoreDailyStatisticQueueService>(MockBehavior.Strict);
            queue.Setup(service => service.EnqueueAsync(
                    It.Is<IEnumerable<DateTime>>(dates => dates.SequenceEqual(new[] { new DateTime(2024, 12, 30) })),
                    "batch-product-sales-analysis", 3, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProductStoreDailyRecalculationSubmitResult
                {
                    SubmittedDates = [new DateTime(2024, 12, 30)],
                    Status = SalesStatisticRefreshStatus.Queued,
                });
            queue.Setup(service => service.EnqueueYearBackfillAsync(
                    It.Is<IEnumerable<DateTime>>(dates => dates.SequenceEqual(new[] { new DateTime(2025, 1, 1) })),
                    "batch-product-sales-analysis", 3, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProductStoreDailyRecalculationSubmitResult
                {
                    SkippedDates = [new DateTime(2025, 1, 1)],
                    Status = SalesStatisticRefreshStatus.Running,
                });
            var service = new BatchProductSalesAnalysisService(db, queue.Object,
                NullLogger<BatchProductSalesAnalysisService>.Instance);

            var response = await service.QueryAsync(new BatchProductSalesQueryRequestDto
            {
                ItemNumbers = ["001"], StartDate = new DateTime(2024, 12, 30), EndDate = new DateTime(2025, 1, 1),
            }, null);

            Assert.Equal(SalesStatisticRefreshStatus.Pending, response.Data!.StatisticStatus);
            var product = Assert.Single(response.Data.Products);
            Assert.Equal(0m, product.Quantity);
            Assert.Equal("partial", response.Data.Coverage.Status);
            Assert.Equal(["2024-12-31"], response.Data.Coverage.ReadyDates);
            Assert.Contains(response.Data.Coverage.PendingDates, row => row.Date == "2024-12-30" && row.Reason == "queued");
            Assert.Contains(response.Data.Coverage.PendingDates, row => row.Date == "2025-01-01" && row.Reason == "active");
            Assert.Contains(response.Data.Warnings, warning => warning.Contains("1 个缺失或未完成统计日期提交", StringComparison.Ordinal));
            Assert.Contains(response.Data.Warnings, warning => warning.Contains("1 个统计日期已有活动队列任务", StringComparison.Ordinal));
            queue.VerifyAll();
        }
        finally
        {
            db.Dispose();
            await connection.CloseAsync();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task QueryAsync_队列提交失败明确告知未排队而不伪称已提交()
    {
        var path = Path.Combine(Path.GetTempPath(), $"batch-product-sales-{Guid.NewGuid():N}.db");
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        try
        {
            db.CodeFirst.InitTables(typeof(Product), typeof(Store), typeof(SalesStatisticRefreshState), typeof(ProductStoreDailySalesStatistic));
            var day = new DateTime(2024, 12, 30);
            await db.Insertable(new Product { ProductCode = "P1", ItemNumber = "001", ProductName = "测试商品", IsDeleted = false }).ExecuteCommandAsync();
            await db.Insertable(new Store { StoreCode = "S1", StoreName = "测试分店", IsDeleted = false }).ExecuteCommandAsync();
            var queue = new Mock<IProductStoreDailyStatisticQueueService>(MockBehavior.Strict);
            queue.Setup(service => service.EnqueueAsync(
                    It.Is<IEnumerable<DateTime>>(dates => dates.SequenceEqual(new[] { day })),
                    "batch-product-sales-analysis", 3, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("queue unavailable"));
            var service = new BatchProductSalesAnalysisService(db, queue.Object,
                NullLogger<BatchProductSalesAnalysisService>.Instance);

            var response = await service.QueryAsync(new BatchProductSalesQueryRequestDto
            {
                ItemNumbers = ["001"], StartDate = day, EndDate = day,
            }, null);

            Assert.Equal(SalesStatisticRefreshStatus.Pending, response.Data!.StatisticStatus);
            var product = Assert.Single(response.Data.Products);
            Assert.Null(product.Quantity);
            Assert.Equal("pending", response.Data.Coverage.Status);
            Assert.Contains(response.Data.Coverage.PendingDates, row => row.Date == "2024-12-30" && row.Reason == "queueFailed");
            Assert.Contains(response.Data.Warnings, warning => warning.Contains("提交失败，尚未排队", StringComparison.Ordinal));
            Assert.DoesNotContain(response.Data.Warnings, warning => warning.Contains("提交到持久队列", StringComparison.Ordinal));
            queue.VerifyAll();
        }
        finally
        {
            db.Dispose();
            await connection.CloseAsync();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
