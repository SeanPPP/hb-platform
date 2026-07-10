using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using AutoMapper;
using BlazorApp.Api.Cache;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesDashboardReportRevenueTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _posmDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _posmConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _posmDb;

    public SalesDashboardReportRevenueTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _posmDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _posmConnection = new SqliteConnection($"Data Source={_posmDbPath}");
        _localConnection.Open();
        _posmConnection.Open();

        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _posmDb = new SqlSugarClient(CreateConnectionConfig(_posmConnection.ConnectionString));
        _localDb.CodeFirst.InitTables(
            typeof(Store),
            typeof(StoreSalesStatistic),
            typeof(HourlySalesStatistic),
            typeof(ProductStoreDailySalesStatistic),
            typeof(AustralianSupplierStoreSalesDetail),
            typeof(ChinaSupplierStoreSalesDetail),
            typeof(StoreSupplierSalesDetail),
            typeof(HBLocalSupplier),
            typeof(ChinaSupplier),
            typeof(Product)
        );
        _posmDb.CodeFirst.InitTables(
            typeof(SalesOrder),
            typeof(SalesOrderDetail),
            typeof(PaymentDetail),
            typeof(PosmProductSupplierMapping)
        );
    }

    [Fact]
    public async Task GetBranchDailyPerformanceAsync_使用统计表并按对比区间偏移配对()
    {
        await SeedStoreSalesStatisticAsync(new DateTime(2026, 7, 1), "S1", "分店一", 100m, 5);
        await SeedStoreSalesStatisticAsync(new DateTime(2026, 7, 2), "S1", "分店一", 150m, 7);
        await SeedStoreSalesStatisticAsync(new DateTime(2026, 7, 1), "S2", "分店二", 999m, 20);
        await SeedStoreSalesStatisticAsync(new DateTime(2025, 7, 1), "S1", "分店一", 80m, 4);
        await SeedStoreSalesStatisticAsync(new DateTime(2025, 7, 2), "S1", "分店一", 200m, 8);
        var service = CreateService();

        var result = await service.GetBranchDailyPerformanceAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 2),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 2),
            },
            new List<string> { "S1" }
        );

        Assert.Collection(
            result,
            row =>
            {
                Assert.Equal(new DateTime(2026, 7, 1), row.Date);
                Assert.Equal("S1", row.BranchCode);
                Assert.Equal(100m, row.Revenue);
                Assert.Equal(80m, row.RevenueLY);
                Assert.Equal(5, row.OrderCount);
                Assert.Equal(4, row.OrderCountLY);
            },
            row =>
            {
                Assert.Equal(new DateTime(2026, 7, 2), row.Date);
                Assert.Equal("S1", row.BranchCode);
                Assert.Equal(150m, row.Revenue);
                Assert.Equal(200m, row.RevenueLY);
                Assert.Equal(7, row.OrderCount);
                Assert.Equal(8, row.OrderCountLY);
            }
        );
    }

    [Fact]
    public async Task GetExecutiveBranchPerformanceAsync_按分店代码聚合避免名称变化拆行()
    {
        await SeedStoreSalesStatisticAsync(new DateTime(2026, 7, 1), "S1", "Store A", 100m, 5);
        await SeedStoreSalesStatisticAsync(new DateTime(2026, 7, 2), "S1", "Store B", 150m, 5);
        await SeedStoreSalesStatisticAsync(new DateTime(2025, 7, 1), "S1", "Store Old", 80m, 4);
        await SeedStoreSalesStatisticAsync(new DateTime(2025, 7, 2), "S1", "Store Old", 70m, 3);
        var service = CreateService();

        var result = await service.GetExecutiveBranchPerformanceAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 2),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 2),
            },
            branchCodes: new List<string> { "S1" }
        );

        var row = Assert.Single(result);
        Assert.Equal("S1", row.BranchCode);
        Assert.Equal(250m, row.Revenue);
        Assert.Equal(150m, row.RevenueLY);
        Assert.Equal(10, row.OrderCount);
        Assert.Equal(7, row.OrderCountLY);
        Assert.Equal(25m, row.Aov);
        Assert.NotEmpty(row.BranchName);
    }

    [Fact]
    public async Task GetExecutiveBranchPerformanceAsync_历史统计缺失时重算并返回去年客单()
    {
        await SeedStoreAsync("S1", "Store A");
        await SeedStoreSalesStatisticAsync(new DateTime(2026, 7, 4), "S1", "Store A", 120m, 6);
        await SeedPosmOrderWithPaymentAsync("old-1", new DateTime(2025, 7, 5, 10, 15, 0), "S1", 40m, 2);
        await SeedPosmOrderWithPaymentAsync("old-2", new DateTime(2025, 7, 5, 11, 30, 0), "S1", 60m, 3);
        var service = CreateService();

        var result = await service.GetExecutiveBranchPerformanceAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
                CompareStartDate = new DateTime(2025, 7, 5),
                CompareEndDate = new DateTime(2025, 7, 5),
            },
            branchCodes: new List<string> { "S1" }
        );

        var row = Assert.Single(result);
        Assert.Equal(100m, row.RevenueLY);
        Assert.Equal(2, row.OrderCountLY);
        Assert.Equal(50m, row.AovLY);
    }

    [Fact]
    public async Task GetExecutiveBranchPerformanceAsync_全分店统计部分缺失时重算整日()
    {
        await SeedStoreAsync("S1", "Store A");
        await SeedStoreAsync("S2", "Store B");
        await SeedStoreSalesStatisticAsync(new DateTime(2026, 7, 4), "S1", "Store A", 10m, 1);
        await SeedPosmOrderWithPaymentAsync("current-1", new DateTime(2026, 7, 4, 10, 15, 0), "S1", 120m, 2);
        await SeedPosmOrderWithPaymentAsync("current-2", new DateTime(2026, 7, 4, 11, 30, 0), "S2", 80m, 1);
        var service = CreateService();

        var result = await service.GetExecutiveBranchPerformanceAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
                CompareStartDate = new DateTime(2025, 7, 5),
                CompareEndDate = new DateTime(2025, 7, 5),
            }
        );

        Assert.Equal(2, result.Count);
        Assert.Contains(result, row => row.BranchCode == "S1" && row.Revenue == 120m);
        Assert.Contains(result, row => row.BranchCode == "S2" && row.Revenue == 80m);
    }

    [Fact]
    public async Task GetExecutiveHourlyTrafficAsync_使用小时统计表按分店和小时聚合()
    {
        await SeedHourlySalesStatisticAsync(new DateTime(2026, 7, 1), 9, "S1", "Store A", 100m, 4);
        await SeedHourlySalesStatisticAsync(new DateTime(2026, 7, 2), 9, "S1", "Store B", 50m, 2);
        await SeedHourlySalesStatisticAsync(new DateTime(2025, 7, 1), 9, "S1", "Store Old", 80m, 3);
        await SeedHourlySalesStatisticAsync(new DateTime(2025, 7, 2), 9, "S1", "Store Old", 20m, 1);
        var service = CreateService();

        var result = await service.GetExecutiveHourlyTrafficAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 2),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 2),
            },
            new List<string> { "S1" }
        );

        var row = Assert.Single(result);
        Assert.Equal("09:00", row.Hour);
        Assert.Equal("S1", row.BranchCode);
        Assert.Equal(150m, row.Revenue);
        Assert.Equal(100m, row.RevenueLY);
        Assert.Equal(6, row.OrderCount);
        Assert.Equal(4, row.OrderCountLY);
        Assert.Equal(100, row.Percentage);
        Assert.True(row.IsPeak);
    }

    [Fact]
    public async Task GetExecutiveHourlyTrafficAsync_全分店查询排除All汇总行()
    {
        await SeedHourlySalesStatisticAsync(new DateTime(2026, 7, 4), 10, "ALL", "All Stores", 300m, 10);
        await SeedHourlySalesStatisticAsync(new DateTime(2026, 7, 4), 10, "S1", "Store A", 120m, 4);
        await SeedHourlySalesStatisticAsync(new DateTime(2025, 7, 5), 10, "ALL", "All Stores", 200m, 8);
        await SeedHourlySalesStatisticAsync(new DateTime(2025, 7, 5), 10, "S1", "Store A", 80m, 2);
        var service = CreateService();

        var result = await service.GetExecutiveHourlyTrafficAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
                CompareStartDate = new DateTime(2025, 7, 5),
                CompareEndDate = new DateTime(2025, 7, 5),
            }
        );

        var row = Assert.Single(result);
        Assert.Equal("S1", row.BranchCode);
        Assert.Equal(120m, row.Revenue);
        Assert.Equal(4, row.OrderCount);
        Assert.Equal(80m, row.RevenueLY);
        Assert.Equal(2, row.OrderCountLY);
    }

    [Fact]
    public async Task GetExecutiveHourlyTrafficAsync_历史统计缺失时重算并返回去年客单数()
    {
        await SeedStoreAsync("S1", "Store A");
        await SeedHourlySalesStatisticAsync(new DateTime(2026, 7, 4), 10, "S1", "Store A", 200m, 4);
        await SeedPosmOrderWithPaymentAsync("old-hour-1", new DateTime(2025, 7, 5, 10, 10, 0), "S1", 35m, 1);
        await SeedPosmOrderWithPaymentAsync("old-hour-2", new DateTime(2025, 7, 5, 10, 40, 0), "S1", 45m, 2);
        var service = CreateService();

        var result = await service.GetExecutiveHourlyTrafficAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
                CompareStartDate = new DateTime(2025, 7, 5),
                CompareEndDate = new DateTime(2025, 7, 5),
            },
            new List<string> { "S1" }
        );

        var row = Assert.Single(result);
        Assert.Equal(80m, row.RevenueLY);
        Assert.Equal(2, row.OrderCountLY);
    }

    [Fact]
    public async Task GetExecutiveHourlyTrafficAsync_本期小时客单缺失时重算当前日期()
    {
        await SeedStoreAsync("S1", "Store A");
        await SeedHourlySalesStatisticAsync(new DateTime(2026, 7, 4), 10, "S1", "Store A", 120m, 0);
        await SeedHourlySalesStatisticAsync(new DateTime(2025, 7, 5), 10, "S1", "Store A", 80m, 2);
        await SeedPosmOrderWithPaymentAsync("current-hour-1", new DateTime(2026, 7, 4, 10, 10, 0), "S1", 50m, 1);
        await SeedPosmOrderWithPaymentAsync("current-hour-2", new DateTime(2026, 7, 4, 10, 40, 0), "S1", 70m, 1);
        var service = CreateService();

        var result = await service.GetExecutiveHourlyTrafficAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
                CompareStartDate = new DateTime(2025, 7, 5),
                CompareEndDate = new DateTime(2025, 7, 5),
            },
            new List<string> { "S1" }
        );

        var row = Assert.Single(result);
        Assert.Equal("10:00", row.Hour);
        Assert.Equal(120m, row.Revenue);
        Assert.Equal(2, row.OrderCount);
        Assert.Equal(80m, row.RevenueLY);
        Assert.Equal(2, row.OrderCountLY);
    }

    [Fact]
    public async Task 营业额报表接口不依赖分店商品统计表()
    {
        await SeedStoreAsync("S1", "Store A");
        await SeedStoreSalesStatisticAsync(new DateTime(2026, 7, 4), "S1", "Store A", 120m, 6);
        await SeedStoreSalesStatisticAsync(new DateTime(2025, 7, 5), "S1", "Store A", 80m, 4);
        await SeedHourlySalesStatisticAsync(new DateTime(2026, 7, 4), 10, "S1", "Store A", 60m, 3);
        await SeedHourlySalesStatisticAsync(new DateTime(2025, 7, 5), 10, "S1", "Store A", 40m, 2);
        await _localDb.Ado.ExecuteCommandAsync("DROP TABLE ProductStoreDailySalesStatistic");
        var service = CreateService();
        var dateRange = new DateRangeDto
        {
            StartDate = new DateTime(2026, 7, 4),
            EndDate = new DateTime(2026, 7, 4),
            CompareStartDate = new DateTime(2025, 7, 5),
            CompareEndDate = new DateTime(2025, 7, 5),
        };

        var summary = await service.GetExecutiveBranchPerformanceAsync(
            dateRange,
            branchCodes: new List<string> { "S1" }
        );
        var hourly = await service.GetExecutiveHourlyTrafficAsync(
            dateRange,
            new List<string> { "S1" }
        );
        var daily = await service.GetBranchDailyPerformanceAsync(
            dateRange,
            new List<string> { "S1" }
        );

        var summaryRow = Assert.Single(summary);
        Assert.Equal(6, summaryRow.OrderCount);
        Assert.Equal(20m, summaryRow.Aov);
        Assert.Equal(4, summaryRow.OrderCountLY);
        Assert.Equal(20m, summaryRow.AovLY);

        var hourlyRow = Assert.Single(hourly);
        Assert.Equal(3, hourlyRow.OrderCount);
        Assert.Equal(2, hourlyRow.OrderCountLY);

        var dailyRow = Assert.Single(daily);
        Assert.Equal(6, dailyRow.OrderCount);
        Assert.Equal(4, dailyRow.OrderCountLY);
    }

    [Fact]
    public async Task GetSupplierSalesRankAsync_返回客单数客单价和同期字段()
    {
        await SeedLocalSupplierAsync("AUS1", "澳洲供应商");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "AUS1", "P-AUS-1", "澳洲商品", 100m, 10, 2);
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "AUS1", "P-AUS-1B", "澳洲商品补充", 25m, 1, 1);
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S2", "AUS1", "P-AUS-2", "澳洲商品二", 50m, 4, 3);
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S1", "AUS1", "P-AUS-1", "澳洲商品", 90m, 9, 3);
        await SeedChinaSupplierAsync("CN-BOUNDARY", "边界中国供应商");
        await SeedSupplierMappingAsync("P-CN-BOUNDARY", "200", "CN-BOUNDARY");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "200", "P-CN-BOUNDARY", "中国边界商品", 999m, 9, 9);
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "CN-BOUNDARY", "P-CN-BOUNDARY-DIRECT", "中国直接编码商品", 777m, 7, 7);
        var service = CreateService();

        var result = await service.GetSupplierSalesRankAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 1),
            },
            branchCodes: new List<string> { "S1", "S2" },
            topN: 1000
        );

        var row = Assert.Single(result, item => item.SupplierCode == "AUS1");
        Assert.Equal("AUS1", row.SupplierCode);
        Assert.Equal(175m, row.TotalAmount);
        Assert.Equal(6, row.OrderCount);
        Assert.Equal(175m / 6m, row.AverageTransaction);
        Assert.Equal(2, row.StoreCount);
        Assert.Equal(90m, row.CompareTotalAmount);
        Assert.Equal(3, row.CompareOrderCount);
        Assert.Equal(30m, row.CompareAverageTransaction);
        var chinaRow = Assert.Single(result, item => item.SupplierCode == "200");
        Assert.Equal(1776m, chinaRow.TotalAmount);
        Assert.Equal(16, chinaRow.OrderCount);
        Assert.DoesNotContain(result, item => item.SupplierCode == "CN-BOUNDARY");
    }

    [Fact]
    public async Task GetSupplierSalesRankAsync_商品统计为空时使用澳洲供应商统计表()
    {
        await SeedLocalSupplierAsync("AUS-FALLBACK", "澳洲兜底供应商");
        await SeedAustralianSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "AUS-FALLBACK", "澳洲兜底供应商", 123m, 7, 3);
        await SeedAustralianSupplierSalesAsync(new DateTime(2025, 7, 5), "S1", "AUS-FALLBACK", "澳洲兜底供应商", 80m, 4, 2);
        var service = CreateService();

        var result = await service.GetSupplierSalesRankAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
                CompareStartDate = new DateTime(2025, 7, 5),
                CompareEndDate = new DateTime(2025, 7, 5),
            },
            branchCodes: new List<string> { "S1" },
            topN: 1000
        );

        var row = Assert.Single(result);
        Assert.Equal("AUS-FALLBACK", row.SupplierCode);
        Assert.Equal(123m, row.TotalAmount);
        Assert.Equal(3, row.OrderCount);
        Assert.Equal(1, row.StoreCount);
        Assert.Equal(80m, row.CompareTotalAmount);
        Assert.Equal(2, row.CompareOrderCount);
    }

    [Fact]
    public async Task GetSupplierSalesRankAsync_商品统计为空时澳洲200不重复叠加中国拆分表()
    {
        await SeedLocalSupplierAsync("200", "hotbargain");
        await SeedChinaSupplierAsync("CN-FALLBACK", "中国兜底供应商");
        await SeedAustralianSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "200", "hotbargain", 100m, 10, 4);
        await SeedChinaSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "CN-FALLBACK", "中国兜底供应商", 100m, 10, 4);
        await SeedAustralianSupplierSalesAsync(new DateTime(2025, 7, 5), "S1", "200", "hotbargain", 50m, 5, 2);
        await SeedChinaSupplierSalesAsync(new DateTime(2025, 7, 5), "S1", "CN-FALLBACK", "中国兜底供应商", 50m, 5, 2);
        var service = CreateService();

        var result = await service.GetSupplierSalesRankAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
                CompareStartDate = new DateTime(2025, 7, 5),
                CompareEndDate = new DateTime(2025, 7, 5),
            },
            branchCodes: new List<string> { "S1" },
            topN: 1000
        );

        var row = Assert.Single(result);
        Assert.Equal("200", row.SupplierCode);
        Assert.Equal(100m, row.TotalAmount);
        Assert.Equal(4, row.OrderCount);
        Assert.Equal(50m, row.CompareTotalAmount);
        Assert.Equal(2, row.CompareOrderCount);
    }

    [Fact]
    public async Task GetSupplierSalesRankAsync_澳洲200同期缺失时从中国拆分表补同期()
    {
        await SeedLocalSupplierAsync("200", "hotbargain");
        await SeedLocalSupplierAsync("AUS-COMPARE", "澳洲同期供应商");
        await SeedChinaSupplierAsync("CN-FALLBACK", "中国兜底供应商");
        await SeedAustralianSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "200", "hotbargain", 100m, 10, 4);
        await SeedAustralianSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "AUS-COMPARE", "澳洲同期供应商", 60m, 6, 2);
        await SeedAustralianSupplierSalesAsync(new DateTime(2025, 7, 5), "S1", "AUS-COMPARE", "澳洲同期供应商", 30m, 3, 1);
        await SeedChinaSupplierSalesAsync(new DateTime(2025, 7, 5), "S1", "CN-FALLBACK", "中国兜底供应商", 50m, 5, 2);
        var service = CreateService();

        var result = await service.GetSupplierSalesRankAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
                CompareStartDate = new DateTime(2025, 7, 5),
                CompareEndDate = new DateTime(2025, 7, 5),
            },
            branchCodes: new List<string> { "S1" },
            topN: 1000
        );

        var row = Assert.Single(result, item => item.SupplierCode == "200");
        Assert.Equal("200", row.SupplierCode);
        Assert.Equal(100m, row.TotalAmount);
        Assert.Equal(50m, row.CompareTotalAmount);
        Assert.Equal(2, row.CompareOrderCount);
        Assert.Contains(result, item => item.SupplierCode == "AUS-COMPARE" && item.CompareTotalAmount == 30m);
    }

    [Fact]
    public async Task GetSupplierSalesRankAsync_同期商品统计缺200时从中国拆分表补200()
    {
        await SeedLocalSupplierAsync("200", "hotbargain");
        await SeedLocalSupplierAsync("AUS1", "澳洲供应商");
        await SeedChinaSupplierAsync("CN-COMPARE", "中国同期供应商");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 4), "S1", "200", "P-CN-CURRENT", "中国当前商品", 100m, 10, 4);
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 4), "S1", "AUS1", "P-AUS-CURRENT", "澳洲当前商品", 60m, 6, 2);
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 5), "S1", "AUS1", "P-AUS-COMPARE", "澳洲同期商品", 30m, 3, 1);
        await SeedChinaSupplierSalesAsync(new DateTime(2025, 7, 5), "S1", "CN-COMPARE", "中国同期供应商", 50m, 5, 2);
        var service = CreateService();

        var result = await service.GetSupplierSalesRankAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
                CompareStartDate = new DateTime(2025, 7, 5),
                CompareEndDate = new DateTime(2025, 7, 5),
            },
            branchCodes: new List<string> { "S1" },
            topN: 1000
        );

        var row = Assert.Single(result, item => item.SupplierCode == "200");
        Assert.Equal(100m, row.TotalAmount);
        Assert.Equal(50m, row.CompareTotalAmount);
        Assert.Equal(2, row.CompareOrderCount);
        Assert.Contains(result, item => item.SupplierCode == "AUS1" && item.CompareTotalAmount == 30m);
    }

    [Fact]
    public async Task GetSupplierSalesRankAsync_当前商品统计缺200时从中国拆分表补200()
    {
        await SeedLocalSupplierAsync("200", "hotbargain");
        await SeedLocalSupplierAsync("AUS1", "澳洲供应商");
        await SeedChinaSupplierAsync("CN-CURRENT", "中国当前供应商");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 4), "S1", "AUS1", "P-AUS-CURRENT", "澳洲当前商品", 60m, 6, 2);
        await SeedChinaSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "CN-CURRENT", "中国当前供应商", 100m, 10, 4);
        var service = CreateService();

        var result = await service.GetSupplierSalesRankAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
            },
            branchCodes: new List<string> { "S1" },
            topN: 1000
        );

        var chinaRow = Assert.Single(result, item => item.SupplierCode == "200");
        Assert.Equal(100m, chinaRow.TotalAmount);
        Assert.Equal(4, chinaRow.OrderCount);
        Assert.Contains(result, item => item.SupplierCode == "AUS1" && item.TotalAmount == 60m);
    }

    [Fact]
    public async Task GetSupplierSalesRankAsync_澳洲200部分商品统计时只补缺失日期()
    {
        await SeedLocalSupplierAsync("200", "hotbargain");
        await SeedChinaSupplierAsync("CN-PARTIAL", "中国部分统计供应商");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 4), "S1", "200", "P-CN-STAT", "中国已统计商品", 100m, 10, 4);
        await SeedChinaSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "CN-PARTIAL", "中国部分统计供应商", 100m, 10, 4);
        await SeedChinaSupplierSalesAsync(new DateTime(2026, 7, 5), "S1", "CN-PARTIAL", "中国部分统计供应商", 50m, 5, 2);
        var service = CreateService();

        var result = await service.GetSupplierSalesRankAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 5),
            },
            branchCodes: new List<string> { "S1" },
            topN: 1000
        );

        var chinaRow = Assert.Single(result, item => item.SupplierCode == "200");
        Assert.Equal(150m, chinaRow.TotalAmount);
        Assert.Equal(6, chinaRow.OrderCount);
    }

    [Fact]
    public async Task GetSupplierStoreSalesAsync_商品统计为空时澳洲200分店下钻不重复叠加中国拆分表()
    {
        await SeedLocalSupplierAsync("200", "hotbargain");
        await SeedChinaSupplierAsync("CN-FALLBACK", "中国兜底供应商");
        await SeedAustralianSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "200", "hotbargain", 100m, 10, 4);
        await SeedChinaSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "CN-FALLBACK", "中国兜底供应商", 100m, 10, 4);
        await SeedAustralianSupplierSalesAsync(new DateTime(2025, 7, 5), "S1", "200", "hotbargain", 50m, 5, 2);
        await SeedChinaSupplierSalesAsync(new DateTime(2025, 7, 5), "S1", "CN-FALLBACK", "中国兜底供应商", 50m, 5, 2);
        var service = CreateService();

        var result = await service.GetSupplierStoreSalesAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
                CompareStartDate = new DateTime(2025, 7, 5),
                CompareEndDate = new DateTime(2025, 7, 5),
            },
            new List<string> { "200" },
            new List<string> { "S1" }
        );

        var row = Assert.Single(result);
        Assert.Equal("200", row.SupplierCode);
        Assert.Equal(100m, row.TotalAmount);
        Assert.Equal(4, row.OrderCount);
        Assert.Equal(50m, row.CompareTotalAmount);
        Assert.Equal(2, row.CompareOrderCount);
    }

    [Fact]
    public async Task GetSupplierStoreSalesAsync_澳洲200同期缺失时从中国拆分表补同期()
    {
        await SeedLocalSupplierAsync("200", "hotbargain");
        await SeedLocalSupplierAsync("AUS-COMPARE", "澳洲同期供应商");
        await SeedChinaSupplierAsync("CN-FALLBACK", "中国兜底供应商");
        await SeedAustralianSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "200", "hotbargain", 100m, 10, 4);
        await SeedAustralianSupplierSalesAsync(new DateTime(2025, 7, 5), "S1", "AUS-COMPARE", "澳洲同期供应商", 30m, 3, 1);
        await SeedChinaSupplierSalesAsync(new DateTime(2025, 7, 5), "S1", "CN-FALLBACK", "中国兜底供应商", 50m, 5, 2);
        var service = CreateService();

        var result = await service.GetSupplierStoreSalesAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
                CompareStartDate = new DateTime(2025, 7, 5),
                CompareEndDate = new DateTime(2025, 7, 5),
            },
            new List<string> { "200" },
            new List<string> { "S1" }
        );

        var row = Assert.Single(result);
        Assert.Equal("200", row.SupplierCode);
        Assert.Equal(100m, row.TotalAmount);
        Assert.Equal(50m, row.CompareTotalAmount);
        Assert.Equal(2, row.CompareOrderCount);
    }

    [Fact]
    public async Task GetSupplierStoreSalesAsync_多供应商同期商品统计缺200时从中国拆分表补200()
    {
        await SeedLocalSupplierAsync("200", "hotbargain");
        await SeedLocalSupplierAsync("AUS1", "澳洲供应商");
        await SeedChinaSupplierAsync("CN-COMPARE", "中国同期供应商");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 4), "S1", "200", "P-CN-CURRENT", "中国当前商品", 100m, 10, 4);
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 4), "S1", "AUS1", "P-AUS-CURRENT", "澳洲当前商品", 60m, 6, 2);
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 5), "S1", "AUS1", "P-AUS-COMPARE", "澳洲同期商品", 30m, 3, 1);
        await SeedChinaSupplierSalesAsync(new DateTime(2025, 7, 5), "S1", "CN-COMPARE", "中国同期供应商", 50m, 5, 2);
        var service = CreateService();

        var result = await service.GetSupplierStoreSalesAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
                CompareStartDate = new DateTime(2025, 7, 5),
                CompareEndDate = new DateTime(2025, 7, 5),
            },
            new List<string> { "200", "AUS1" },
            new List<string> { "S1" }
        );

        var chinaRow = Assert.Single(result, item => item.SupplierCode == "200");
        Assert.Equal(100m, chinaRow.TotalAmount);
        Assert.Equal(50m, chinaRow.CompareTotalAmount);
        Assert.Equal(2, chinaRow.CompareOrderCount);
        Assert.Contains(result, item => item.SupplierCode == "AUS1" && item.CompareTotalAmount == 30m);
    }

    [Fact]
    public async Task GetSupplierStoreSalesAsync_多供应商当前商品统计缺200时从中国拆分表补200()
    {
        await SeedLocalSupplierAsync("200", "hotbargain");
        await SeedLocalSupplierAsync("AUS1", "澳洲供应商");
        await SeedChinaSupplierAsync("CN-CURRENT", "中国当前供应商");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 4), "S1", "AUS1", "P-AUS-CURRENT", "澳洲当前商品", 60m, 6, 2);
        await SeedChinaSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "CN-CURRENT", "中国当前供应商", 100m, 10, 4);
        var service = CreateService();

        var result = await service.GetSupplierStoreSalesAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
            },
            new List<string> { "200", "AUS1" },
            new List<string> { "S1" }
        );

        var chinaRow = Assert.Single(result, item => item.SupplierCode == "200");
        Assert.Equal(100m, chinaRow.TotalAmount);
        Assert.Equal(4, chinaRow.OrderCount);
        Assert.Contains(result, item => item.SupplierCode == "AUS1" && item.TotalAmount == 60m);
    }

    [Fact]
    public async Task GetSupplierSalesRankAsync_澳洲报表把中国货汇总到200()
    {
        await SeedLocalSupplierAsync("AUS1", "澳洲供应商");
        await SeedLocalSupplierAsync("200", "hotbargain");
        await SeedChinaSupplierAsync("CN1", "中国供应商一");
        await SeedChinaSupplierAsync("CN2", "中国供应商二");
        await SeedSupplierMappingAsync("P-CN-LEGACY", "200", "CN1");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "AUS1", "P-AUS", "澳洲商品", 100m, 10, 2);
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "200", "P-CN-LEGACY", "中国旧统计商品", 60m, 6, 3);
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S2", "CN2", "P-CN-DIRECT", "中国直接统计商品", 40m, 4, 2);
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S1", "200", "P-CN-LEGACY", "中国旧统计商品同期", 30m, 3, 1);
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S2", "CN2", "P-CN-DIRECT", "中国直接统计商品同期", 20m, 2, 1);
        var service = CreateService();

        var result = await service.GetSupplierSalesRankAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 1),
            },
            branchCodes: new List<string> { "S1", "S2" },
            topN: 1000
        );

        var chinaRow = Assert.Single(result, row => row.SupplierCode == "200");
        Assert.Equal("hotbargain", chinaRow.SupplierName);
        Assert.Equal(100m, chinaRow.TotalAmount);
        Assert.Equal(10, chinaRow.TotalQuantity);
        Assert.Equal(5, chinaRow.OrderCount);
        Assert.Equal(2, chinaRow.StoreCount);
        Assert.Equal(50m, chinaRow.CompareTotalAmount);
        Assert.Equal(2, chinaRow.CompareOrderCount);
        Assert.DoesNotContain(result, row => row.SupplierCode == "CN2");
        Assert.Contains(result, row => row.SupplierCode == "AUS1" && row.TotalAmount == 100m);
    }

    [Fact]
    public async Task GetSupplierStoreSalesAsync_澳洲200分店下钻包含中国货()
    {
        await SeedLocalSupplierAsync("200", "hotbargain");
        await SeedChinaSupplierAsync("CN1", "中国供应商一");
        await SeedChinaSupplierAsync("CN2", "中国供应商二");
        await SeedSupplierMappingAsync("P-CN-LEGACY", "200", "CN1");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "200", "P-CN-LEGACY", "中国旧统计商品", 60m, 6, 3);
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S2", "CN2", "P-CN-DIRECT", "中国直接统计商品", 40m, 4, 2);
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S1", "200", "P-CN-LEGACY", "中国旧统计商品同期", 30m, 3, 1);
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S2", "CN2", "P-CN-DIRECT", "中国直接统计商品同期", 20m, 2, 1);
        var service = CreateService();

        var result = await service.GetSupplierStoreSalesAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 1),
            },
            new List<string> { "200" },
            new List<string> { "S1", "S2" }
        );

        Assert.Equal(2, result.Count);
        var s1 = Assert.Single(result, row => row.BranchCode == "S1");
        Assert.Equal("200", s1.SupplierCode);
        Assert.Equal(60m, s1.TotalAmount);
        Assert.Equal(3, s1.OrderCount);
        Assert.Equal(30m, s1.CompareTotalAmount);
        var s2 = Assert.Single(result, row => row.BranchCode == "S2");
        Assert.Equal("200", s2.SupplierCode);
        Assert.Equal(40m, s2.TotalAmount);
        Assert.Equal(2, s2.OrderCount);
        Assert.Equal(20m, s2.CompareTotalAmount);
    }

    [Fact]
    public async Task GetChinaSupplierSalesRankAsync_全量排行不受大量Posm映射影响()
    {
        await SeedChinaSupplierAsync("CN-BULK", "中国大供应商");
        await SeedSupplierMappingsAsync(
            Enumerable
                .Range(0, 2205)
                .Select(index => ($"P-CN-BULK-{index}", "200", "CN-BULK"))
        );
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "200", "P-CN-BULK-2204", "中国大供应商商品", 10m, 1, 1);
        var service = CreateService();

        var result = await service.GetChinaSupplierSalesRankAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
            },
            branchCodes: new List<string> { "S1" },
            topN: 1000
        );

        var row = Assert.Single(result);
        Assert.Equal("CN-BULK", row.SupplierCode);
        Assert.Equal(10m, row.TotalAmount);
    }

    [Fact]
    public async Task GetChinaSupplierSalesRankAsync_返回客单数客单价和同期字段()
    {
        await SeedChinaSupplierAsync("CN1", "中国供应商");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "CN1", "P-CN-1", "中国商品", 120m, 12, 4);
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S1", "CN1", "P-CN-1", "中国商品", 50m, 5, 2);
        var service = CreateService();

        var result = await service.GetChinaSupplierSalesRankAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 1),
            },
            branchCodes: new List<string> { "S1" },
            topN: 1000
        );

        var row = Assert.Single(result);
        Assert.Equal("CN1", row.SupplierCode);
        Assert.Equal(120m, row.TotalAmount);
        Assert.Equal(4, row.OrderCount);
        Assert.Equal(30m, row.AverageTransaction);
        Assert.Equal(50m, row.CompareTotalAmount);
        Assert.Equal(2, row.CompareOrderCount);
        Assert.Equal(25m, row.CompareAverageTransaction);
    }

    [Fact]
    public async Task GetChinaSupplierSalesRankAsync_商品统计为空时使用中国供应商统计表()
    {
        await SeedChinaSupplierAsync("CN-FALLBACK", "中国兜底供应商");
        await SeedChinaSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "CN-FALLBACK", "中国兜底供应商", 210m, 9, 5);
        await SeedChinaSupplierSalesAsync(new DateTime(2025, 7, 5), "S1", "CN-FALLBACK", "中国兜底供应商", 70m, 3, 2);
        var service = CreateService();

        var result = await service.GetChinaSupplierSalesRankAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
                CompareStartDate = new DateTime(2025, 7, 5),
                CompareEndDate = new DateTime(2025, 7, 5),
            },
            branchCodes: new List<string> { "S1" },
            topN: 1000
        );

        var row = Assert.Single(result);
        Assert.Equal("CN-FALLBACK", row.SupplierCode);
        Assert.Equal(210m, row.TotalAmount);
        Assert.Equal(5, row.OrderCount);
        Assert.Equal(1, row.StoreCount);
        Assert.Equal(70m, row.CompareTotalAmount);
        Assert.Equal(2, row.CompareOrderCount);
    }

    [Fact]
    public async Task GetChinaSupplierSalesRankAsync_字典和映射为空时仍读取中国供应商统计表()
    {
        await SeedChinaSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "CN-STAT-ONLY", "统计表供应商", 66m, 6, 2);
        var service = CreateService();

        var result = await service.GetChinaSupplierSalesRankAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
            },
            branchCodes: new List<string> { "S1" },
            topN: 1000
        );

        var row = Assert.Single(result);
        Assert.Equal("CN-STAT-ONLY", row.SupplierCode);
        Assert.Equal(66m, row.TotalAmount);
    }

    [Fact]
    public async Task GetChinaSupplierSalesRankAsync_商品统计存在但映射缺失时不走供应商统计兜底()
    {
        await SeedChinaSupplierAsync("CN-MISSING-MAP", "缺映射供应商");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 4), "S1", "200", "P-MISSING-MAP", "缺映射商品", 99m, 9, 3);
        await SeedChinaSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "CN-MISSING-MAP", "缺映射供应商", 210m, 9, 5);
        var service = CreateService();

        var result = await service.GetChinaSupplierSalesRankAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
            },
            branchCodes: new List<string> { "S1" },
            topN: 1000
        );

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSupplierStoreSalesAsync_返回分店客单数客单价和同期字段()
    {
        await SeedStoreAsync("S1", "分店一");
        await SeedLocalSupplierAsync("AUS1", "澳洲供应商");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "AUS1", "P-AUS-3", "分店澳洲商品", 100m, 10, 2);
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S1", "AUS1", "P-AUS-3", "分店澳洲商品", 80m, 8, 4);
        var service = CreateService();

        var result = await service.GetSupplierStoreSalesAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 1),
            },
            new List<string> { "AUS1" },
            new List<string> { "S1" }
        );

        var row = Assert.Single(result);
        Assert.Equal("分店一", row.BranchName);
        Assert.Equal(2, row.OrderCount);
        Assert.Equal(50m, row.AverageTransaction);
        Assert.Equal(80m, row.CompareTotalAmount);
        Assert.Equal(4, row.CompareOrderCount);
        Assert.Equal(20m, row.CompareAverageTransaction);
    }

    [Fact]
    public async Task GetSupplierStoreSalesAsync_商品统计为空时使用澳洲供应商统计表()
    {
        await SeedStoreAsync("S1", "分店一");
        await SeedLocalSupplierAsync("AUS-FALLBACK", "澳洲兜底供应商");
        await SeedAustralianSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "AUS-FALLBACK", "澳洲兜底供应商", 123m, 7, 3);
        await SeedAustralianSupplierSalesAsync(new DateTime(2025, 7, 5), "S1", "AUS-FALLBACK", "澳洲兜底供应商", 80m, 4, 2);
        var service = CreateService();

        var result = await service.GetSupplierStoreSalesAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
                CompareStartDate = new DateTime(2025, 7, 5),
                CompareEndDate = new DateTime(2025, 7, 5),
            },
            new List<string> { "AUS-FALLBACK" },
            new List<string> { "S1" }
        );

        var row = Assert.Single(result);
        Assert.Equal("分店一", row.BranchName);
        Assert.Equal(123m, row.TotalAmount);
        Assert.Equal(3, row.OrderCount);
        Assert.Equal(80m, row.CompareTotalAmount);
        Assert.Equal(2, row.CompareOrderCount);
    }

    [Fact]
    public async Task GetChinaSupplierStoreSalesAsync_返回分店客单数客单价同期字段且缓存不串澳洲()
    {
        await SeedStoreAsync("S1", "分店一");
        await SeedLocalSupplierAsync("AUS1", "澳洲供应商");
        await SeedChinaSupplierAsync("CN1", "中国供应商");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "AUS1", "P-AUS-SUP1", "澳洲供应商商品", 999m, 9, 9);
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "CN1", "P-CN-SUP1", "中国供应商商品", 120m, 12, 4);
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S1", "CN1", "P-CN-SUP1", "中国供应商商品", 50m, 5, 2);
        var service = CreateService();
        var dateRange = new DateRangeDto
        {
            StartDate = new DateTime(2026, 7, 1),
            EndDate = new DateTime(2026, 7, 1),
            CompareStartDate = new DateTime(2025, 7, 1),
            CompareEndDate = new DateTime(2025, 7, 1),
        };

        await service.GetSupplierStoreSalesAsync(dateRange, new List<string> { "AUS1" }, new List<string> { "S1" });
        var result = await service.GetChinaSupplierStoreSalesAsync(
            dateRange,
            new List<string> { "CN1" },
            new List<string> { "S1" }
        );

        var row = Assert.Single(result);
        Assert.Equal("分店一", row.BranchName);
        Assert.Equal(120m, row.TotalAmount);
        Assert.Equal(4, row.OrderCount);
        Assert.Equal(30m, row.AverageTransaction);
        Assert.Equal(50m, row.CompareTotalAmount);
        Assert.Equal(2, row.CompareOrderCount);
        Assert.Equal(25m, row.CompareAverageTransaction);
    }

    [Fact]
    public async Task GetChinaSupplierStoreSalesAsync_兼容旧统计表200供应商映射()
    {
        await SeedStoreAsync("S1", "分店一");
        await SeedChinaSupplierAsync("CN-LEGACY", "中国旧统计供应商");
        await SeedSupplierMappingAsync("P-CN-LEGACY", "200", "CN-LEGACY");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "200", "P-CN-LEGACY", "旧统计中国商品", 88m, 8, 2);
        var service = CreateService();

        var result = await service.GetChinaSupplierStoreSalesAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
            },
            new List<string> { "CN-LEGACY" },
            new List<string> { "S1" }
        );

        var row = Assert.Single(result);
        Assert.Equal("CN-LEGACY", row.SupplierCode);
        Assert.Equal(88m, row.TotalAmount);
    }

    [Fact]
    public async Task GetChinaSupplierStoreSalesAsync_商品统计为空时使用中国供应商统计表()
    {
        await SeedStoreAsync("S1", "分店一");
        await SeedChinaSupplierAsync("CN-FALLBACK", "中国兜底供应商");
        await SeedChinaSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "CN-FALLBACK", "中国兜底供应商", 210m, 9, 5);
        await SeedChinaSupplierSalesAsync(new DateTime(2025, 7, 5), "S1", "CN-FALLBACK", "中国兜底供应商", 70m, 3, 2);
        var service = CreateService();

        var result = await service.GetChinaSupplierStoreSalesAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
                CompareStartDate = new DateTime(2025, 7, 5),
                CompareEndDate = new DateTime(2025, 7, 5),
            },
            new List<string> { "CN-FALLBACK" },
            new List<string> { "S1" }
        );

        var row = Assert.Single(result);
        Assert.Equal("分店一", row.BranchName);
        Assert.Equal(210m, row.TotalAmount);
        Assert.Equal(5, row.OrderCount);
        Assert.Equal(70m, row.CompareTotalAmount);
        Assert.Equal(2, row.CompareOrderCount);
    }

    [Fact]
    public async Task GetChinaSupplierStoreSalesAsync_商品统计存在但映射缺失时不走供应商统计兜底()
    {
        await SeedStoreAsync("S1", "分店一");
        await SeedChinaSupplierAsync("CN-MISSING-MAP", "缺映射供应商");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 4), "S1", "200", "P-MISSING-MAP", "缺映射商品", 99m, 9, 3);
        await SeedChinaSupplierSalesAsync(new DateTime(2026, 7, 4), "S1", "CN-MISSING-MAP", "缺映射供应商", 210m, 9, 5);
        var service = CreateService();

        var result = await service.GetChinaSupplierStoreSalesAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 4),
                EndDate = new DateTime(2026, 7, 4),
            },
            new List<string> { "CN-MISSING-MAP" },
            new List<string> { "S1" }
        );

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetProductSalesByAllBranchesAsync_按分店范围汇总金额均价且缓存不串数据()
    {
        await SeedStoreAsync("S1", "分店一");
        await SeedStoreAsync("S2", "分店二");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "AUS1", "P1", "商品一", 40m, 2, 1);
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S2", "AUS1", "P1", "商品一", 90m, 3, 2);
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S1", "AUS1", "P1", "商品一", 30m, 1, 1);
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S2", "AUS1", "P1", "商品一", 60m, 2, 1);
        var service = CreateService();
        var dateRange = new DateRangeDto
        {
            StartDate = new DateTime(2026, 7, 1),
            EndDate = new DateTime(2026, 7, 1),
            CompareStartDate = new DateTime(2025, 7, 1),
            CompareEndDate = new DateTime(2025, 7, 1),
        };

        var first = await service.GetProductSalesByAllBranchesAsync(dateRange, "P1", new List<string> { "S1" });
        var second = await service.GetProductSalesByAllBranchesAsync(dateRange, "P1", new List<string> { "S2" });

        var firstRow = Assert.Single(first);
        Assert.Equal("S1", firstRow.BranchCode);
        Assert.Equal(2, firstRow.Quantity);
        Assert.Equal(40m, firstRow.SalesAmount);
        Assert.Equal(30m, firstRow.CompareSalesAmount);
        Assert.Equal(20m, firstRow.AverageUnitPrice);

        var secondRow = Assert.Single(second);
        Assert.Equal("S2", secondRow.BranchCode);
        Assert.Equal(3, secondRow.Quantity);
        Assert.Equal(90m, secondRow.SalesAmount);
        Assert.Equal(60m, secondRow.CompareSalesAmount);
        Assert.Equal(30m, secondRow.AverageUnitPrice);
        Assert.Equal(0, secondRow.DiscountedQuantity);
    }

    [Fact]
    public async Task GetProductSalesByAllBranchesAsync_返回同期独有分店()
    {
        await SeedStoreAsync("S1", "分店一");
        await SeedStoreAsync("S2", "分店二");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "AUS1", "P1", "商品一", 40m, 2, 1);
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S2", "AUS1", "P1", "商品一", 60m, 3, 1);
        var service = CreateService();

        var result = await service.GetProductSalesByAllBranchesAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 1),
            },
            "P1"
        );

        Assert.Equal(2, result.Count);
        var compareOnlyRow = Assert.Single(result, row => row.BranchCode == "S2");
        Assert.Equal(0, compareOnlyRow.Quantity);
        Assert.Equal(0m, compareOnlyRow.SalesAmount);
        Assert.Equal(60m, compareOnlyRow.CompareSalesAmount);
        Assert.Equal(3, GetIntProperty(compareOnlyRow, "CompareQuantity"));
    }

    [Fact]
    public async Task GetEnhancedSalesProductDetailsAsync_按货号过滤商品明细()
    {
        await SeedProductAsync("P1", "HB001", "BAR001");
        await SeedProductAsync("P2", "HB002", "BAR002");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "AUS1", "P1", "商品一", 40m, 2, 1, barcode: "BAR001");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "AUS1", "P2", "商品二", 100m, 5, 1, barcode: "BAR002");
        var service = CreateService();

        var result = await service.GetEnhancedSalesProductDetailsAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
            },
            productSearch: "HB001"
        );

        var row = Assert.Single(result.Data);
        Assert.Equal(1, result.Total);
        Assert.Equal("P1", row.ProductCode);
        Assert.Equal("HB001", row.ItemNumber);
        Assert.Equal(2, row.Quantity);
        Assert.Equal(40m, row.SalesAmount);
    }

    [Fact]
    public async Task GetEnhancedSalesProductDetailsAsync_按Posm明细条码过滤商品明细()
    {
        await SeedProductAsync("P3", "HB003", "MASTER-BAR");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "AUS1", "P3", "扫码商品", 90m, 3, 1, barcode: "SCAN-ONLY-123");
        var service = CreateService();

        var result = await service.GetEnhancedSalesProductDetailsAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
            },
            productSearch: "SCAN-ONLY"
        );

        var row = Assert.Single(result.Data);
        Assert.Equal("P3", row.ProductCode);
        Assert.Equal("HB003", row.ItemNumber);
        Assert.Equal(90m, row.SalesAmount);
    }

    [Fact]
    public async Task GetEnhancedSalesProductDetailsAsync_供应商筛选下搜索同时支持澳洲和中国()
    {
        await SeedProductAsync("P4", "HB004", "BAR004");
        await SeedProductAsync("P5", "HB005", "BAR005");
        await SeedSupplierMappingAsync("P4", "200", "CN1");
        await SeedSupplierMappingAsync("P5", "200", "CN2");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "AUS1", "P4", "供应商商品", 80m, 4, 1, barcode: "BAR004");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "AUS2", "P5", "其它商品", 120m, 6, 1, barcode: "BAR005");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "200", "P4", "中国供应商商品", 80m, 4, 1, barcode: "BAR004");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "200", "P5", "中国其它商品", 120m, 6, 1, barcode: "BAR005");
        var service = CreateService();
        var dateRange = new DateRangeDto
        {
            StartDate = new DateTime(2026, 7, 1),
            EndDate = new DateTime(2026, 7, 1),
        };

        var australia = await service.GetEnhancedSalesProductDetailsAsync(
            dateRange,
            localSupplierCodes: new List<string> { "AUS1" },
            productSearch: "HB004"
        );
        var china = await service.GetEnhancedSalesProductDetailsAsync(
            dateRange,
            chinaSupplierCodes: new List<string> { "CN1" },
            productSearch: "HB004"
        );

        Assert.Equal("P4", Assert.Single(australia.Data).ProductCode);
        Assert.Equal("P4", Assert.Single(china.Data).ProductCode);
        Assert.Equal(1, australia.Total);
        Assert.Equal(1, china.Total);
    }

    [Fact]
    public async Task GetEnhancedSalesProductDetailsAsync_澳洲200筛选包含中国货商品()
    {
        await SeedChinaSupplierAsync("CN1", "中国供应商一");
        await SeedChinaSupplierAsync("CN2", "中国供应商二");
        await SeedProductAsync("P-CN-LEGACY", "HB-CN-LEGACY", "BAR-CN-LEGACY");
        await SeedProductAsync("P-CN-DIRECT", "HB-CN-DIRECT", "BAR-CN-DIRECT");
        await SeedSupplierMappingAsync("P-CN-LEGACY", "200", "CN1");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "200", "P-CN-LEGACY", "中国旧统计商品", 60m, 6, 3, barcode: "BAR-CN-LEGACY");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S2", "CN2", "P-CN-DIRECT", "中国直接统计商品", 40m, 4, 2, barcode: "BAR-CN-DIRECT");
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S2", "CN2", "P-CN-DIRECT", "中国直接统计商品同期", 20m, 2, 1, barcode: "BAR-CN-DIRECT");
        var service = CreateService();

        var result = await service.GetEnhancedSalesProductDetailsAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 1),
            },
            branchCodes: new List<string> { "S1", "S2" },
            localSupplierCodes: new List<string> { "200" },
            pageSize: 20
        );

        Assert.Equal(2, result.Total);
        var legacyRow = Assert.Single(result.Data, row => row.ProductCode == "P-CN-LEGACY");
        Assert.Equal(60m, legacyRow.SalesAmount);
        var directRow = Assert.Single(result.Data, row => row.ProductCode == "P-CN-DIRECT");
        Assert.Equal(40m, directRow.SalesAmount);
        Assert.Equal(20m, directRow.SalesAmountLY);
    }

    [Fact]
    public async Task GetEnhancedSalesProductDetailsAsync_澳洲200筛选搜索中国货商品()
    {
        await SeedChinaSupplierAsync("CN1", "中国供应商一");
        await SeedChinaSupplierAsync("CN2", "中国供应商二");
        await SeedProductAsync("P-CN-LEGACY", "HB-CN-LEGACY", "BAR-CN-LEGACY");
        await SeedProductAsync("P-CN-DIRECT", "HB-CN-DIRECT", "BAR-CN-DIRECT");
        await SeedSupplierMappingAsync("P-CN-LEGACY", "200", "CN1");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "200", "P-CN-LEGACY", "中国旧统计商品", 60m, 6, 3, barcode: "BAR-CN-LEGACY");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S2", "CN2", "P-CN-DIRECT", "中国直接统计商品", 40m, 4, 2, barcode: "BAR-CN-DIRECT");
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S2", "CN2", "P-CN-DIRECT", "中国直接统计商品同期", 20m, 2, 1, barcode: "BAR-CN-DIRECT");
        var service = CreateService();

        var result = await service.GetEnhancedSalesProductDetailsAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 1),
            },
            branchCodes: new List<string> { "S1", "S2" },
            localSupplierCodes: new List<string> { "200" },
            pageSize: 20,
            productSearch: "HB-CN-DIRECT"
        );

        var row = Assert.Single(result.Data);
        Assert.Equal(1, result.Total);
        Assert.Equal("P-CN-DIRECT", row.ProductCode);
        Assert.Equal(40m, row.SalesAmount);
        Assert.Equal(20m, row.SalesAmountLY);
    }

    [Fact]
    public async Task GetEnhancedSalesProductDetailsAsync_指定中国供应商大量映射不超参数且不混入其它供应商()
    {
        await SeedChinaSupplierAsync("CN-BIG", "中国大供应商");
        await SeedChinaSupplierAsync("CN-OTHER", "其它中国供应商");
        await SeedSupplierMappingsAsync(
            Enumerable
                .Range(0, 2205)
                .Select(index => ($"P-CN-BIG-{index}", "200", "CN-BIG"))
        );
        await SeedSupplierMappingAsync("P-CN-OTHER", "200", "CN-OTHER");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "200", "P-CN-BIG-2204", "大供应商旧统计", 10m, 1, 1);
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S1", "200", "P-CN-BIG-2204", "大供应商旧统计同期", 5m, 1, 1);
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "CN-BIG", "P-CN-BIG-DIRECT", "大供应商新统计", 20m, 2, 1);
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "200", "P-CN-OTHER", "其它供应商商品", 999m, 9, 9);
        var service = CreateService();

        var result = await service.GetEnhancedSalesProductDetailsAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 1),
            },
            branchCodes: new List<string> { "S1" },
            chinaSupplierCodes: new List<string> { "CN-BIG" },
            pageSize: 20
        );

        Assert.Equal(2, result.Total);
        Assert.DoesNotContain(result.Data, row => row.ProductCode == "P-CN-OTHER");
        var legacyRow = Assert.Single(result.Data, row => row.ProductCode == "P-CN-BIG-2204");
        Assert.Equal(10m, legacyRow.SalesAmount);
        Assert.Equal(5m, legacyRow.SalesAmountLY);
        var directRow = Assert.Single(result.Data, row => row.ProductCode == "P-CN-BIG-DIRECT");
        Assert.Equal(20m, directRow.SalesAmount);
    }

    [Fact]
    public async Task GetEnhancedSalesProductDetailsAsync_大量映射加宽泛搜索不超参数且不混入其它供应商()
    {
        await SeedChinaSupplierAsync("CN-BIG", "中国大供应商");
        await SeedChinaSupplierAsync("CN-OTHER", "其它中国供应商");
        await SeedProductsAsync(
            Enumerable
                .Range(0, 2205)
                .Select(index => ($"P-CN-BIG-{index}", $"HB-CN-BIG-{index}", $"BAR-CN-BIG-{index}"))
        );
        await SeedProductAsync("P-CN-BIG-DIRECT", "HB-CN-BIG-DIRECT", "BAR-CN-BIG-DIRECT");
        await SeedProductAsync("P-CN-OTHER", "HB-CN-BIG-OTHER", "BAR-CN-BIG-OTHER");
        await SeedSupplierMappingsAsync(
            Enumerable
                .Range(0, 2205)
                .Select(index => ($"P-CN-BIG-{index}", "200", "CN-BIG"))
        );
        await SeedSupplierMappingAsync("P-CN-OTHER", "200", "CN-OTHER");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "200", "P-CN-BIG-2204", "大供应商旧统计", 10m, 1, 1, barcode: "BAR-CN-BIG-2204");
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S1", "200", "P-CN-BIG-2204", "大供应商旧统计同期", 5m, 1, 1, barcode: "BAR-CN-BIG-2204");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "CN-BIG", "P-CN-BIG-DIRECT", "大供应商新统计", 20m, 2, 1, barcode: "BAR-CN-BIG-DIRECT");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "200", "P-CN-OTHER", "其它供应商商品", 999m, 9, 9, barcode: "BAR-CN-BIG-OTHER");
        var service = CreateService();

        var result = await service.GetEnhancedSalesProductDetailsAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 1),
            },
            branchCodes: new List<string> { "S1" },
            chinaSupplierCodes: new List<string> { "CN-BIG" },
            pageSize: 20,
            productSearch: "HB-CN-BIG"
        );

        Assert.Equal(2, result.Total);
        Assert.DoesNotContain(result.Data, row => row.ProductCode == "P-CN-OTHER");
        Assert.Contains(result.Data, row =>
            row.ProductCode == "P-CN-BIG-2204"
            && row.SalesAmount == 10m
            && row.SalesAmountLY == 5m
        );
        Assert.Contains(result.Data, row =>
            row.ProductCode == "P-CN-BIG-DIRECT"
            && row.SalesAmount == 20m
        );
    }

    [Fact]
    public async Task GetEnhancedSalesProductDetailsAsync_搜索同时过滤对比期商品()
    {
        await SeedProductAsync("P6", "HB006", "BAR006");
        await SeedProductAsync("P7", "HB007", "BAR007");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "AUS1", "P6", "匹配商品", 40m, 2, 1, barcode: "BAR006");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "AUS1", "P7", "当前期不匹配", 100m, 5, 1, barcode: "BAR007");
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S1", "AUS1", "P6", "对比期匹配", 30m, 1, 1, barcode: "BAR006");
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S1", "AUS1", "P7", "对比期不匹配", 140m, 7, 1, barcode: "BAR007");
        var service = CreateService();

        var result = await service.GetEnhancedSalesProductDetailsAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 1),
            },
            productSearch: "HB006"
        );

        var row = Assert.Single(result.Data);
        Assert.Equal(1, result.Total);
        Assert.Equal("P6", row.ProductCode);
        Assert.Equal(2, row.Quantity);
        Assert.Equal(40m, row.SalesAmount);
        Assert.Equal(1, row.QuantityLY);
        Assert.Equal(30m, row.SalesAmountLY);
    }

    [Fact]
    public async Task GetEnhancedSalesProductDetailsAsync_返回当前期和同期商品并集()
    {
        await SeedProductAsync("P8", "HB008", "BAR008");
        await SeedProductAsync("P9", "HB009", "BAR009");
        await SeedProductStoreDailySalesAsync(new DateTime(2026, 7, 1), "S1", "AUS1", "P8", "当前商品", 80m, 4, 1, barcode: "BAR008");
        await SeedProductStoreDailySalesAsync(new DateTime(2025, 7, 1), "S1", "AUS1", "P9", "同期商品", 50m, 2, 1, barcode: "BAR009");
        var service = CreateService();

        var result = await service.GetEnhancedSalesProductDetailsAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 1),
            }
        );

        Assert.Equal(2, result.Total);
        var currentRow = Assert.Single(result.Data, row => row.ProductCode == "P8");
        Assert.Equal(4, currentRow.Quantity);
        Assert.Equal(0, currentRow.QuantityLY);
        var compareOnlyRow = Assert.Single(result.Data, row => row.ProductCode == "P9");
        Assert.Equal("HB009", compareOnlyRow.ItemNumber);
        Assert.Equal(0, compareOnlyRow.Quantity);
        Assert.Equal(50m, compareOnlyRow.SalesAmountLY);
        Assert.Equal(2, compareOnlyRow.QuantityLY);
    }

    [Fact]
    public void EnhancedProductDetail_搜索词参与缓存键但不写入日志()
    {
        var logger = new RecordingLogger();
        SalesDashboardCacheKeys.SetLogger(logger);

        try
        {
            var dateRange = new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
            };

            var first = SalesDashboardCacheKeys.EnhancedProductDetail(
                dateRange,
                new List<string> { "S1" },
                localSupplierCodes: null,
                chinaSupplierCodes: null,
                pageIndex: 1,
                pageSize: 20,
                productSearch: "  SECRET-BARCODE  "
            );
            var sameNormalized = SalesDashboardCacheKeys.EnhancedProductDetail(
                dateRange,
                new List<string> { "S1" },
                localSupplierCodes: null,
                chinaSupplierCodes: null,
                pageIndex: 1,
                pageSize: 20,
                productSearch: "SECRET-BARCODE"
            );
            var otherSearch = SalesDashboardCacheKeys.EnhancedProductDetail(
                dateRange,
                new List<string> { "S1" },
                localSupplierCodes: null,
                chinaSupplierCodes: null,
                pageIndex: 1,
                pageSize: 20,
                productSearch: "OTHER-BARCODE"
            );

            Assert.Equal(first, sameNormalized);
            Assert.NotEqual(first, otherSearch);
            Assert.Contains(logger.Messages, message => message.Contains("HasProductSearch=True", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("SECRET-BARCODE", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("OTHER-BARCODE", StringComparison.Ordinal));
        }
        finally
        {
            SalesDashboardCacheKeys.SetLogger(NullLogger.Instance);
        }
    }

    [Fact]
    public async Task GetBranchDailyPerformance_普通用户请求无权限分店返回空数组()
    {
        var serviceMock = new Mock<ISalesDashboardReactService>();
        var controller = CreateController(
            serviceMock.Object,
            CreateUserService(new[] { "S1" })
        );

        var response = await controller.GetBranchDailyPerformance(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 7),
            branchCodes: new List<string> { "S2" }
        );

        var data = ExtractAnonymousData<List<object>>(AssertOk(response).Value);
        Assert.Empty(data);
        serviceMock.Verify(
            service => service.GetBranchDailyPerformanceAsync(
                It.IsAny<DateRangeDto>(),
                It.IsAny<List<string>?>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task GetBranchDailyPerformance_统计表查询失败时返回服务器错误()
    {
        await _localDb.Ado.ExecuteCommandAsync("DROP TABLE StoreSalesStatistic");
        var controller = CreateController(CreateService(), CreateUserService(new[] { "S1" }));

        var response = await controller.GetBranchDailyPerformance(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 7),
            branchCodes: new List<string> { "S1" }
        );

        var objectResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetExecutiveBranchPerformance_统计表查询失败时返回服务器错误()
    {
        await _localDb.Ado.ExecuteCommandAsync("DROP TABLE StoreSalesStatistic");
        var controller = CreateController(CreateService(), CreateUserService(new[] { "S1" }));

        var response = await controller.GetExecutiveBranchPerformance(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 7),
            branchCodes: new List<string> { "S1" }
        );

        var objectResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetExecutiveHourlyTraffic_统计表查询失败时返回服务器错误()
    {
        await _localDb.Ado.ExecuteCommandAsync("DROP TABLE HourlySalesStatistic");
        var controller = CreateController(CreateService(), CreateUserService(new[] { "S1" }));

        var response = await controller.GetExecutiveHourlyTraffic(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 1),
            branchCodes: new List<string> { "S1" }
        );

        var objectResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetSupplierStoreSales_统计表查询失败时返回服务器错误()
    {
        await _localDb.Ado.ExecuteCommandAsync("DROP TABLE ProductStoreDailySalesStatistic");
        var controller = CreateController(CreateService(), CreateUserService(new[] { "S1" }));

        var response = await controller.GetSupplierStoreSales(
            new List<string> { "AUS1" },
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 1),
            branchCodes: new List<string> { "S1" }
        );

        var objectResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetChinaSupplierStoreSales_统计表查询失败时返回服务器错误()
    {
        await SeedSupplierMappingAsync("P-CN-ERROR", "200", "CN-ERROR");
        await _localDb.Ado.ExecuteCommandAsync("DROP TABLE ProductStoreDailySalesStatistic");
        var controller = CreateController(CreateService(), CreateUserService(new[] { "S1" }));

        var response = await controller.GetChinaSupplierStoreSales(
            new List<string> { "CN-ERROR" },
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 1),
            branchCodes: new List<string> { "S1" }
        );

        var objectResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetExecutiveBranchPerformance_普通用户未传分店时只查用户分店()
    {
        List<string>? capturedBranchCodes = null;
        int? capturedTopN = -1;
        var serviceMock = new Mock<ISalesDashboardReactService>();
        serviceMock
            .Setup(service => service.GetExecutiveBranchPerformanceAsync(
                It.IsAny<DateRangeDto>(),
                It.IsAny<int?>(),
                It.IsAny<List<string>?>()
            ))
            .Callback<DateRangeDto, int?, List<string>?>((_, topN, branchCodes) =>
            {
                capturedTopN = topN;
                capturedBranchCodes = branchCodes;
            })
            .ReturnsAsync(new List<ExecutiveBranchPerformanceDto>());
        var controller = CreateController(serviceMock.Object, CreateUserService(new[] { "S1", "S3" }));

        await controller.GetExecutiveBranchPerformance(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 7)
        );

        Assert.Equal(new[] { "S1", "S3" }, capturedBranchCodes);
        Assert.Null(capturedTopN);
    }

    [Fact]
    public async Task GetExecutiveHourlyTraffic_普通用户请求分店时取权限交集()
    {
        List<string>? capturedBranchCodes = null;
        var serviceMock = new Mock<ISalesDashboardReactService>();
        serviceMock
            .Setup(service => service.GetExecutiveHourlyTrafficAsync(
                It.IsAny<DateRangeDto>(),
                It.IsAny<List<string>?>()
            ))
            .Callback<DateRangeDto, List<string>?>((_, branchCodes) =>
                capturedBranchCodes = branchCodes
            )
            .ReturnsAsync(new List<ExecutiveHourlyTrafficDto>());
        var controller = CreateController(serviceMock.Object, CreateUserService(new[] { "S1", "S3" }));

        await controller.GetExecutiveHourlyTraffic(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 1),
            branchCodes: new List<string> { "S1", "S2" }
        );

        var branchCode = Assert.Single(capturedBranchCodes!);
        Assert.Equal("S1", branchCode);
    }

    [Fact]
    public async Task GetProductSalesByAllBranches_普通用户请求分店时取权限交集()
    {
        List<string>? capturedBranchCodes = null;
        var serviceMock = new Mock<ISalesDashboardReactService>();
        serviceMock
            .Setup(service => service.GetProductSalesByAllBranchesAsync(
                It.IsAny<DateRangeDto>(),
                "P1",
                It.IsAny<List<string>?>()
            ))
            .Callback<DateRangeDto, string, List<string>?>((_, _, branchCodes) =>
                capturedBranchCodes = branchCodes
            )
            .ReturnsAsync(new List<ProductBranchSalesDto>());
        var controller = CreateController(serviceMock.Object, CreateUserService(new[] { "S1", "S3" }));

        await controller.GetProductSalesByAllBranches(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 1),
            productCode: "P1",
            branchCodes: new List<string> { "S1", "S2" }
        );

        var branchCode = Assert.Single(capturedBranchCodes!);
        Assert.Equal("S1", branchCode);
    }

    [Fact]
    public async Task GetSupplierSalesRank_普通用户无分店时不调用服务层全量查询()
    {
        var serviceMock = new Mock<ISalesDashboardReactService>();
        var controller = CreateController(serviceMock.Object, CreateUserService(Array.Empty<string>()));

        var response = await controller.GetSupplierSalesRank(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 1)
        );

        var data = ExtractAnonymousData<List<object>>(AssertOk(response).Value);
        Assert.Empty(data);
        serviceMock.Verify(
            service => service.GetSupplierSalesRankAsync(
                It.IsAny<DateRangeDto>(),
                It.IsAny<List<string>?>(),
                It.IsAny<int>(),
                It.IsAny<string?>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task GetEnhancedSalesProductDetails_普通用户无分店时不调用服务层全量查询()
    {
        var serviceMock = new Mock<ISalesDashboardReactService>();
        var controller = CreateController(serviceMock.Object, CreateUserService(Array.Empty<string>()));

        var response = await controller.GetEnhancedSalesProductDetails(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 1),
            pageIndex: 1,
            pageSize: 50
        );

        var data = ExtractAnonymousData<PagedSalesProductDetailWithDiscountDto>(AssertOk(response).Value);
        Assert.Empty(data.Data);
        serviceMock.Verify(
            service => service.GetEnhancedSalesProductDetailsAsync(
                It.IsAny<DateRangeDto>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task GetEnhancedSalesProductDetails_普通用户未传分店时只查用户分店()
    {
        List<string>? capturedBranchCodes = null;
        string? capturedProductSearch = null;
        var serviceMock = new Mock<ISalesDashboardReactService>();
        serviceMock
            .Setup(service => service.GetEnhancedSalesProductDetailsAsync(
                It.IsAny<DateRangeDto>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>()
            ))
            .Callback<DateRangeDto, List<string>?, List<string>?, List<string>?, int, int, string?>(
                (_, branchCodes, _, _, _, _, productSearch) =>
                {
                    capturedBranchCodes = branchCodes;
                    capturedProductSearch = productSearch;
                }
            )
            .ReturnsAsync(new PagedSalesProductDetailWithDiscountDto());
        var controller = CreateController(serviceMock.Object, CreateUserService(new[] { "S1", "S3" }));

        await controller.GetEnhancedSalesProductDetails(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 1),
            pageIndex: 1,
            pageSize: 50,
            productSearch: "HB001"
        );

        Assert.Equal(new[] { "S1", "S3" }, capturedBranchCodes);
        Assert.Equal("HB001", capturedProductSearch);
    }

    private async Task SeedStoreSalesStatisticAsync(
        DateTime date,
        string branchCode,
        string branchName,
        decimal totalAmount,
        int orderCount
    )
    {
        await _localDb.Insertable(new StoreSalesStatistic
        {
            Date = date,
            BranchCode = branchCode,
            BranchName = branchName,
            TotalAmount = totalAmount,
            OrderCount = orderCount,
            AverageOrderValue = orderCount > 0 ? totalAmount / orderCount : 0,
            TotalQuantity = orderCount,
            CustomerCount = orderCount,
            UpdateTime = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedHourlySalesStatisticAsync(
        DateTime date,
        int hour,
        string branchCode,
        string branchName,
        decimal totalAmount,
        int orderCount
    )
    {
        await _localDb.Insertable(new HourlySalesStatistic
        {
            Date = date,
            Hour = hour,
            BranchCode = branchCode,
            BranchName = branchName,
            TotalAmount = totalAmount,
            OrderCount = orderCount,
            AverageOrderValue = orderCount > 0 ? totalAmount / orderCount : 0,
            TotalQuantity = orderCount,
            CustomerCount = orderCount,
            UpdateTime = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreAsync(string storeCode, string storeName)
    {
        await _localDb.Insertable(new Store
        {
            StoreGUID = $"store-{storeCode}",
            StoreCode = storeCode,
            StoreName = storeName,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedLocalSupplierAsync(string supplierCode, string supplierName)
    {
        await _localDb.Insertable(new HBLocalSupplier
        {
            Guid = $"local-{supplierCode}",
            LocalSupplierCode = supplierCode,
            Name = supplierName,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedChinaSupplierAsync(string supplierCode, string supplierName)
    {
        await _localDb.Insertable(new ChinaSupplier
        {
            Guid = $"china-{supplierCode}",
            SupplierCode = supplierCode,
            SupplierName = supplierName,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedProductStoreDailySalesAsync(
        DateTime date,
        string branchCode,
        string supplierCode,
        string productCode,
        string productName,
        decimal totalAmount,
        int totalQuantity,
        int orderCount,
        string? barcode = null
    )
    {
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = date.Date,
            BranchCode = branchCode,
            SupplierCode = supplierCode,
            ProductCode = productCode,
            ProductName = productName,
            Barcode = barcode,
            TotalAmount = totalAmount,
            TotalQuantity = totalQuantity,
            OrderCount = orderCount,
            CostSource = "Test",
            UpdateTime = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedAustralianSupplierSalesAsync(
        DateTime date,
        string branchCode,
        string supplierCode,
        string supplierName,
        decimal totalAmount,
        int totalQuantity,
        int orderCount
    )
    {
        await _localDb.Insertable(new AustralianSupplierStoreSalesDetail
        {
            Date = date,
            BranchCode = branchCode,
            SupplierCode = supplierCode,
            SupplierName = supplierName,
            TotalAmount = totalAmount,
            TotalQuantity = totalQuantity,
            OrderCount = orderCount,
            UpdateTime = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedChinaSupplierSalesAsync(
        DateTime date,
        string branchCode,
        string supplierCode,
        string supplierName,
        decimal totalAmount,
        int totalQuantity,
        int orderCount
    )
    {
        await _localDb.Insertable(new ChinaSupplierStoreSalesDetail
        {
            Date = date,
            BranchCode = branchCode,
            SupplierCode = supplierCode,
            SupplierName = supplierName,
            TotalAmount = totalAmount,
            TotalQuantity = totalQuantity,
            OrderCount = orderCount,
            UpdateTime = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreSupplierSalesAsync(
        DateTime date,
        string branchCode,
        string supplierCode,
        string supplierName,
        decimal totalAmount,
        int totalQuantity,
        int orderCount
    )
    {
        await _localDb.Insertable(new StoreSupplierSalesDetail
        {
            Date = date,
            BranchCode = branchCode,
            SupplierCode = supplierCode,
            SupplierName = supplierName,
            TotalAmount = totalAmount,
            TotalQuantity = totalQuantity,
            OrderCount = orderCount,
            UpdateTime = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedSalesOrderAsync(string orderGuid, DateTime orderTime, string branchCode)
    {
        await _posmDb.Insertable(new SalesOrder
        {
            OrderGuid = orderGuid,
            OrderTime = orderTime,
            BranchCode = branchCode,
            Status = 1,
            ActualAmount = 0m,
            TotalAmount = 0m,
        }).ExecuteCommandAsync();
    }

    private async Task SeedSalesOrderDetailAsync(
        string detailGuid,
        string orderGuid,
        string productCode,
        int quantity,
        decimal actualAmount,
        decimal discountAmount,
        string? barcode = null,
        string? productName = null
    )
    {
        await _posmDb.Insertable(new SalesOrderDetail
        {
            OrderDetailGuid = detailGuid,
            OrderGuid = orderGuid,
            ProductCode = productCode,
            ProductName = productName ?? productCode,
            Barcode = barcode,
            Quantity = quantity,
            ActualAmount = actualAmount,
            DiscountAmount = discountAmount,
            Subtotal = actualAmount,
        }).ExecuteCommandAsync();
    }

    private async Task SeedPaymentDetailAsync(string paymentGuid, string orderGuid, decimal amount)
    {
        await _posmDb.Insertable(new PaymentDetail
        {
            PaymentGuid = paymentGuid,
            OrderGuid = orderGuid,
            Amount = amount,
            PaymentMethod = 1,
            CreatedTime = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedPosmOrderWithPaymentAsync(
        string orderGuid,
        DateTime orderTime,
        string branchCode,
        decimal amount,
        int quantity
    )
    {
        await SeedSalesOrderAsync(orderGuid, orderTime, branchCode);
        await SeedSalesOrderDetailAsync(
            $"detail-{orderGuid}",
            orderGuid,
            $"product-{orderGuid}",
            quantity,
            amount,
            0m
        );
        await SeedPaymentDetailAsync($"payment-{orderGuid}", orderGuid, amount);
    }

    private async Task SeedProductAsync(string productCode, string itemNumber, string barcode)
    {
        await _localDb.Insertable(new Product
        {
            UUID = productCode,
            ProductCode = productCode,
            ItemNumber = itemNumber,
            Barcode = barcode,
            ProductName = itemNumber,
        }).ExecuteCommandAsync();
    }

    private async Task SeedProductsAsync(IEnumerable<(string ProductCode, string ItemNumber, string Barcode)> rows)
    {
        var products = rows
            .Select(row => new Product
            {
                UUID = row.ProductCode,
                ProductCode = row.ProductCode,
                ItemNumber = row.ItemNumber,
                Barcode = row.Barcode,
                ProductName = row.ItemNumber,
            })
            .ToList();

        if (products.Any())
        {
            await _localDb.Insertable(products).ExecuteCommandAsync();
        }
    }

    private async Task SeedSupplierMappingAsync(string productCode, string localSupplierCode, string chinaSupplierCode)
    {
        await _posmDb.Insertable(new PosmProductSupplierMapping
        {
            ProductCode = productCode,
            LocalSupplierCode = localSupplierCode,
            ChinaSupplierCode = chinaSupplierCode,
            LastUpdateTime = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedSupplierMappingsAsync(
        IEnumerable<(string ProductCode, string LocalSupplierCode, string ChinaSupplierCode)> rows
    )
    {
        var entities = rows
            .Select(row => new PosmProductSupplierMapping
            {
                ProductCode = row.ProductCode,
                LocalSupplierCode = row.LocalSupplierCode,
                ChinaSupplierCode = row.ChinaSupplierCode,
                LastUpdateTime = DateTime.UtcNow,
            })
            .ToList();
        await _posmDb.Insertable(entities).ExecuteCommandAsync();
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private SalesDashboardReactService CreateService()
    {
        var localContext = CreateSqlSugarContext(_localDb);
        var posmContext = CreatePosmSqlSugarContext(_posmDb);
        var services = new ServiceCollection()
            .AddSingleton(localContext)
            .AddSingleton(posmContext)
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton<ILogger<SalesStatisticsJobService>>(
                NullLogger<SalesStatisticsJobService>.Instance
            )
            .AddScoped<SalesStatisticsJobService>()
            .BuildServiceProvider();

        return new SalesDashboardReactService(
            localContext,
            posmContext,
            Mock.Of<IMapper>(),
            NullLogger<SalesDashboardReactService>.Instance,
            new MemoryCache(new MemoryCacheOptions()),
            services.GetRequiredService<IServiceScopeFactory>()
        );
    }

    private static IUserService CreateUserService(IEnumerable<string> storeCodes)
    {
        var stores = storeCodes
            .Select(code => new UserStoreDto { StoreCode = code })
            .ToList();
        var userServiceMock = new Mock<IUserService>();
        userServiceMock
            .Setup(service => service.GetUserByGuidAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserDetailDto>.OK(new UserDetailDto
            {
                UserGUID = "user-1",
                Username = "tester",
                Stores = stores,
            }));
        return userServiceMock.Object;
    }

    private static SalesDashboardController CreateController(
        ISalesDashboardReactService service,
        IUserService userService
    )
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
            },
            "TestAuth"
        ));

        var controller = new SalesDashboardController(
            service,
            NullLogger<SalesDashboardController>.Instance,
            userService,
            Mock.Of<ISalesDashboardCacheWarmer>()
        );
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static T ExtractAnonymousData<T>(object? value)
    {
        Assert.NotNull(value);
        var dataProperty = value!.GetType().GetProperty("data");
        Assert.NotNull(dataProperty);
        var data = dataProperty!.GetValue(value);
        return Assert.IsType<T>(data);
    }

    private static OkObjectResult AssertOk(IActionResult result)
    {
        return Assert.IsType<OkObjectResult>(result);
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString)
    {
        return new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        };
    }

    private static int GetIntProperty(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<int>(property.GetValue(target));
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static POSMSqlSugarContext CreatePosmSqlSugarContext(ISqlSugarClient db)
    {
        var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(POSMSqlSugarContext));
        typeof(POSMSqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _localConnection.Dispose();
        _posmConnection.Dispose();
        if (File.Exists(_localDbPath)) SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        if (File.Exists(_posmDbPath)) SqliteTempFileCleanup.DeleteIfExists(_posmDbPath);
    }
}
