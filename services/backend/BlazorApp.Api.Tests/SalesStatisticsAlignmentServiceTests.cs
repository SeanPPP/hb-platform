using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesStatisticsAlignmentServiceTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _posmDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _posmConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _posmDb;

    public SalesStatisticsAlignmentServiceTests()
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
            typeof(DailySalesStatistic),
            typeof(HourlySalesStatistic),
            typeof(StoreSalesStatistic),
            typeof(SupplierSalesStatistic),
            typeof(StoreSupplierSalesDetail),
            typeof(ProductStoreDailySalesStatistic),
            typeof(AustralianSupplierStoreSalesDetail),
            typeof(ChinaSupplierStoreSalesDetail),
            typeof(SalesStatisticRefreshState),
            typeof(ScheduledTaskLease)
        );
        _posmDb.CodeFirst.InitTables(
            typeof(SalesOrder),
            typeof(SalesOrderDetail),
            typeof(PaymentDetail)
        );
    }

    [Fact]
    public async Task GetDailyAlignmentAsync_八张表一致时_总体为Aligned()
    {
        var targetDate = new DateTime(2026, 7, 5);
        await SeedAlignedMetricsAsync(targetDate);

        var result = await CreateService().GetDailyAlignmentAsync(targetDate, targetDate);
        var row = Assert.Single(result.Rows);

        Assert.Equal(StatisticsAlignmentStatus.Aligned, row.OverallStatus);
        Assert.Equal(8, row.Details.Count);
        Assert.All(
            row.Details.Where(detail => !detail.DiagnosticOnly),
            detail => Assert.Equal(StatisticsAlignmentStatus.Aligned, detail.Status)
        );
    }

    [Fact]
    public async Task GetDailyAlignmentAsync_核心统计表无数据时_返回Missing()
    {
        var targetDate = new DateTime(2026, 7, 5);
        await _localDb.Insertable(new StoreSalesStatistic
        {
            Date = targetDate,
            BranchCode = "S1",
            BranchName = "Store 1",
            TotalAmount = 100m,
            TotalQuantity = 5,
            OrderCount = 2,
            UpdateTime = targetDate.AddHours(8),
        }).ExecuteCommandAsync();

        var result = await CreateService().GetDailyAlignmentAsync(targetDate, targetDate);
        var row = Assert.Single(result.Rows);
        var dailyDetail = Assert.Single(row.Details, detail => detail.TableName == "DailySalesStatistic");

        Assert.Equal(StatisticsAlignmentStatus.Missing, row.OverallStatus);
        Assert.Equal(StatisticsAlignmentStatus.Missing, dailyDetail.Status);
        Assert.Contains("没有统计记录", dailyDetail.Reason);
        Assert.Contains("补算", dailyDetail.Remediation);
    }

    [Fact]
    public async Task GetDailyAlignmentAsync_POSM水位晚于统计时间时_返回Stale()
    {
        var targetDate = new DateTime(2026, 7, 5);
        await SeedAlignedMetricsAsync(targetDate, updateTime: targetDate.AddHours(8));
        await _posmDb.Insertable(new SalesOrder
        {
            OrderGuid = "ORDER-STale",
            OrderTime = targetDate.AddHours(10),
            Status = 1,
            LastUploadTime = targetDate.AddHours(23),
        }).ExecuteCommandAsync();

        var result = await CreateService().GetDailyAlignmentAsync(targetDate, targetDate);
        var row = Assert.Single(result.Rows);

        Assert.Equal(StatisticsAlignmentStatus.Stale, row.OverallStatus);
    }

    [Fact]
    public async Task GetDailyAlignmentAsync_同日期租约运行中时_总体为Running()
    {
        var targetDate = new DateTime(2026, 7, 5);
        await SeedAlignedMetricsAsync(targetDate);
        await _localDb.Insertable(new ScheduledTaskLease
        {
            TaskType = SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType,
            ScopeKey = targetDate.ToString("yyyy-MM-dd"),
            Status = ScheduledTaskLeaseStatus.Running,
            OwnerInstanceId = "api-a",
            LeaseUntilUtc = DateTime.UtcNow.AddMinutes(30),
            StartedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        var result = await CreateService().GetDailyAlignmentAsync(targetDate, targetDate);
        var row = Assert.Single(result.Rows);

        Assert.Equal(StatisticsAlignmentStatus.Running, row.OverallStatus);
        Assert.Contains("运行中", row.Reason);
        Assert.Contains("等待", row.Remediation);
    }

    [Fact]
    public async Task GetDailyAlignmentAsync_门店供应商为国内供应商行时_不误判Mismatch()
    {
        var targetDate = new DateTime(2026, 7, 5);
        await SeedAlignedMetricsAsync(targetDate);
        await _localDb.Deleteable<StoreSupplierSalesDetail>()
            .Where(row => row.Date == targetDate)
            .ExecuteCommandAsync();
        await _localDb.Insertable(new StoreSupplierSalesDetail
        {
            Date = targetDate,
            BranchCode = "S1",
            SupplierCode = "C1",
            SupplierName = "China 1",
            IsDomestic = true,
            TotalAmount = 100m,
            TotalQuantity = 5,
            OrderCount = 2,
            UpdateTime = targetDate.AddDays(1),
        }).ExecuteCommandAsync();

        var result = await CreateService().GetDailyAlignmentAsync(targetDate, targetDate);
        var row = Assert.Single(result.Rows);
        var storeSupplierDetail = Assert.Single(
            row.Details,
            detail => detail.TableName == "StoreSupplierSalesDetail"
        );

        Assert.Equal(StatisticsAlignmentStatus.Aligned, row.OverallStatus);
        Assert.Equal(StatisticsAlignmentStatus.Aligned, storeSupplierDetail.Status);
    }

    public void Dispose()
    {
        _localDb.Dispose();
        _posmDb.Dispose();
        _localConnection.Dispose();
        _posmConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        SqliteTempFileCleanup.DeleteIfExists(_posmDbPath);
    }

    private async Task SeedAlignedMetricsAsync(DateTime date, DateTime? updateTime = null)
    {
        var updatedAt = updateTime ?? date.AddDays(1);
        await _localDb.Insertable(new DailySalesStatistic
        {
            Date = date,
            TotalAmount = 100m,
            TotalQuantity = 5,
            OrderCount = 2,
            SkuCount = 2,
            CustomerCount = 2,
            AverageOrderValue = 50m,
            UpdateTime = updatedAt,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new HourlySalesStatistic
        {
            Date = date,
            Hour = 10,
            BranchCode = "ALL",
            BranchName = "All Stores",
            TotalAmount = 100m,
            TotalQuantity = 5,
            OrderCount = 2,
            CustomerCount = 2,
            AverageOrderValue = 50m,
            UpdateTime = updatedAt,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new StoreSalesStatistic
        {
            Date = date,
            BranchCode = "S1",
            BranchName = "Store 1",
            TotalAmount = 100m,
            TotalQuantity = 5,
            OrderCount = 2,
            CustomerCount = 2,
            AverageOrderValue = 50m,
            UpdateTime = updatedAt,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new SupplierSalesStatistic
        {
            Date = date,
            SupplierCode = "L1",
            SupplierName = "Local 1",
            IsDomestic = false,
            TotalAmount = 100m,
            TotalQuantity = 5,
            StoreCount = 1,
            OrderCount = 2,
            UpdateTime = updatedAt,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new StoreSupplierSalesDetail
        {
            Date = date,
            BranchCode = "S1",
            SupplierCode = "L1",
            SupplierName = "Local 1",
            IsDomestic = false,
            TotalAmount = 100m,
            TotalQuantity = 5,
            OrderCount = 2,
            UpdateTime = updatedAt,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = date,
            BranchCode = "S1",
            SupplierCode = "L1",
            ProductCode = "P1",
            TotalAmount = 100m,
            TotalQuantity = 5,
            OrderCount = 2,
            UpdateTime = updatedAt,
            LastSourceUploadTime = updatedAt,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new AustralianSupplierStoreSalesDetail
        {
            Date = date,
            BranchCode = "S1",
            SupplierCode = "L1",
            SupplierName = "Local 1",
            TotalAmount = 60m,
            TotalQuantity = 3,
            OrderCount = 1,
            UpdateTime = updatedAt,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new ChinaSupplierStoreSalesDetail
        {
            Date = date,
            BranchCode = "S1",
            SupplierCode = "C1",
            SupplierName = "China 1",
            TotalAmount = 40m,
            TotalQuantity = 2,
            OrderCount = 1,
            UpdateTime = updatedAt,
        }).ExecuteCommandAsync();
    }

    private SalesStatisticsAlignmentService CreateService()
    {
        var context = CreateSqlSugarContext(_localDb);
        return new SalesStatisticsAlignmentService(
            context,
            CreatePosmSqlSugarContext(_posmDb),
            new ScheduledTaskLeaseService(
                context,
                Options.Create(new ScheduledTaskOptions { InstanceId = "api-a" }),
                NullLogger<ScheduledTaskLeaseService>.Instance
            ),
            Mock.Of<IServiceScopeFactory>(),
            NullLogger<SalesStatisticsAlignmentService>.Instance
        );
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

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static POSMSqlSugarContext CreatePosmSqlSugarContext(ISqlSugarClient db)
    {
        var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(POSMSqlSugarContext));
        typeof(POSMSqlSugarContext)
            .GetField("_db", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }
}
