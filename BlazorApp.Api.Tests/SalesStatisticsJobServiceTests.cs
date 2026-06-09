using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesStatisticsJobServiceTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _posmDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _posmConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _posmDb;

    public SalesStatisticsJobServiceTests()
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
            typeof(Product),
            typeof(WarehouseProduct),
            typeof(StoreRetailPrice),
            typeof(StoreSalesStatistic),
            typeof(StoreSupplierSalesDetail),
            typeof(ProductStoreDailySalesStatistic),
            typeof(SalesStatisticRefreshState)
        );
        _posmDb.CodeFirst.InitTables(typeof(SalesOrder), typeof(SalesOrderDetail), typeof(POSM_设备注册信息表));
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_供应商为空时不应写入主表()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-UNMATCHED");
        await SeedSaleAsync(
            orderGuid: "ORDER-UNMATCHED",
            detailGuid: "DETAIL-UNMATCHED",
            productCode: "P-UNMATCHED",
            branchCode: "1018",
            orderTime: targetDate.AddHours(9),
            quantity: 3,
            actualAmount: 12.34m,
            supplierCode: string.Empty
        );

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var recordCount = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1018" && x.ProductCode == "P-UNMATCHED")
            .CountAsync();

        Assert.Equal(0, recordCount);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_商品统计与分店营业额统计一致时即使供应商统计不一致也应为Fresh()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-FRESH");
        await SeedSaleAsync(
            orderGuid: "ORDER-FRESH",
            detailGuid: "DETAIL-FRESH",
            productCode: "P-FRESH",
            branchCode: "1018",
            orderTime: targetDate.AddHours(10),
            quantity: 5,
            actualAmount: 100m,
            supplierCode: "200"
        );
        await SeedStoreSalesStatisticAsync(targetDate, "1018", 100m, 5);
        await SeedStoreSupplierSalesDetailAsync(targetDate, "1018", "200", 23m, 1);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var state = await LoadRefreshStateAsync(targetDate);
        var rowCount = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate)
            .CountAsync();

        Assert.True(state != null, $"未生成状态行，商品统计行数={rowCount}");
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state!.Status);
        Assert.True(string.IsNullOrWhiteSpace(state.ErrorMessage));
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_商品统计与分店营业额统计不一致时应标记Failed并写明原因()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-FAILED");
        await SeedSaleAsync(
            orderGuid: "ORDER-FAILED",
            detailGuid: "DETAIL-FAILED",
            productCode: "P-FAILED",
            branchCode: "1018",
            orderTime: targetDate.AddHours(11),
            quantity: 6,
            actualAmount: 88.63m,
            supplierCode: "200"
        );
        await SeedStoreSalesStatisticAsync(targetDate, "1018", 220m, 6);
        await SeedStoreSupplierSalesDetailAsync(targetDate, "1018", "200", 88.63m, 6);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var state = await LoadRefreshStateAsync(targetDate);
        var rowCount = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate)
            .CountAsync();

        Assert.True(state != null, $"未生成状态行，商品统计行数={rowCount}");
        Assert.Equal(SalesStatisticRefreshStatus.Failed, state!.Status);
        Assert.Contains("商品统计与分店营业额统计不一致", state.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("2026-05-01", state.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("1018", state.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_营业额差异在容差内时应Fresh()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-TOLERANCE");
        await SeedSaleAsync(
            orderGuid: "ORDER-TOLERANCE",
            detailGuid: "DETAIL-TOLERANCE",
            productCode: "P-TOLERANCE",
            branchCode: "1004",
            orderTime: targetDate.AddHours(11),
            quantity: 1,
            actualAmount: 2153.38m,
            supplierCode: "200"
        );
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 2154.04m, 1);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var state = await LoadRefreshStateAsync(targetDate);
        Assert.NotNull(state);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state!.Status);
        Assert.True(string.IsNullOrWhiteSpace(state.ErrorMessage));
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_营业额差异超过百分之一且超过100时仍应Failed()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-TOLERANCE-FAILED");
        await SeedSaleAsync(
            orderGuid: "ORDER-TOLERANCE-FAILED",
            detailGuid: "DETAIL-TOLERANCE-FAILED",
            productCode: "P-TOLERANCE-FAILED",
            branchCode: "1004",
            orderTime: targetDate.AddHours(12),
            quantity: 1,
            actualAmount: 2020m,
            supplierCode: "200"
        );
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 2154.04m, 1);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var state = await LoadRefreshStateAsync(targetDate);
        Assert.NotNull(state);
        Assert.Equal(SalesStatisticRefreshStatus.Failed, state!.Status);
        Assert.Contains("金额差", state.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_分店营业额数量不一致但金额一致时不应Failed()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-QUANTITY");
        await SeedSaleAsync(
            orderGuid: "ORDER-QUANTITY",
            detailGuid: "DETAIL-QUANTITY",
            productCode: "P-QUANTITY",
            branchCode: "1018",
            orderTime: targetDate.AddHours(12),
            quantity: 4,
            actualAmount: 60m,
            supplierCode: "200"
        );
        await SeedStoreSalesStatisticAsync(targetDate, "1018", 60m, 999);
        await SeedStoreSupplierSalesDetailAsync(targetDate, "1018", "200", 60m, 4);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var state = await LoadRefreshStateAsync(targetDate);
        var rowCount = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate)
            .CountAsync();

        Assert.True(state != null, $"未生成状态行，商品统计行数={rowCount}");
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state!.Status);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_商品统计分店缺少营业额基准时应Failed()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-MISSING-STORE");
        await SeedSaleAsync(
            orderGuid: "ORDER-MISSING-STORE",
            detailGuid: "DETAIL-MISSING-STORE",
            productCode: "P-MISSING-STORE",
            branchCode: "1018",
            orderTime: targetDate.AddHours(13),
            quantity: 2,
            actualAmount: 30m,
            supplierCode: "200"
        );

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var state = await LoadRefreshStateAsync(targetDate);
        Assert.NotNull(state);
        Assert.Equal(SalesStatisticRefreshStatus.Failed, state!.Status);
        Assert.Contains("分店营业额统计缺失", state.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("1018", state.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecoverTimedOutProductStoreDailyRecalculationJobsAsync_只恢复超时执行中任务()
    {
        var nowUtc = new DateTime(2026, 6, 8, 6, 0, 0, DateTimeKind.Utc);
        var timeout = TimeSpan.FromMinutes(30);
        await SeedRefreshStateAsync(
            new DateTime(2026, 6, 1),
            SalesStatisticRefreshStatus.Queued,
            requestedAtUtc: nowUtc.AddMinutes(-31),
            jobId: Guid.NewGuid(),
            errorMessage: "旧排队任务"
        );
        await SeedRefreshStateAsync(
            new DateTime(2026, 6, 2),
            SalesStatisticRefreshStatus.Running,
            requestedAtUtc: nowUtc.AddHours(-1),
            startedAtUtc: nowUtc.AddMinutes(-31),
            lastCheckedAtUtc: nowUtc.AddMinutes(-1),
            jobId: Guid.NewGuid(),
            errorMessage: "旧运行任务"
        );
        await SeedRefreshStateAsync(
            new DateTime(2026, 6, 3),
            SalesStatisticRefreshStatus.Queued,
            requestedAtUtc: nowUtc.AddMinutes(-5),
            jobId: Guid.NewGuid()
        );
        await SeedRefreshStateAsync(
            new DateTime(2026, 6, 4),
            SalesStatisticRefreshStatus.Running,
            requestedAtUtc: nowUtc.AddMinutes(-20),
            startedAtUtc: nowUtc.AddMinutes(-5),
            jobId: Guid.NewGuid()
        );
        await SeedRefreshStateAsync(
            new DateTime(2026, 6, 5),
            SalesStatisticRefreshStatus.Fresh,
            lastCheckedAtUtc: nowUtc.AddHours(-2)
        );
        await SeedRefreshStateAsync(
            new DateTime(2026, 6, 6),
            SalesStatisticRefreshStatus.Failed,
            lastCheckedAtUtc: nowUtc.AddHours(-2),
            errorMessage: "对账失败"
        );
        await SeedRefreshStateAsync(
            new DateTime(2026, 6, 7),
            SalesStatisticRefreshStatus.Stale,
            lastCheckedAtUtc: nowUtc.AddHours(-2)
        );
        await SeedRefreshStateAsync(
            new DateTime(2026, 6, 8),
            SalesStatisticRefreshStatus.Pending,
            lastCheckedAtUtc: nowUtc.AddHours(-2)
        );

        var recoveredCount = await CreateService()
            .RecoverTimedOutProductStoreDailyRecalculationJobsAsync(timeout, nowUtc);

        Assert.Equal(2, recoveredCount);
        var recoveredQueued = await LoadRefreshStateAsync(new DateTime(2026, 6, 1));
        Assert.Equal(SalesStatisticRefreshStatus.Pending, recoveredQueued!.Status);
        Assert.Null(recoveredQueued.JobId);
        Assert.Null(recoveredQueued.StartedAtUtc);
        Assert.Null(recoveredQueued.CompletedAtUtc);
        Assert.Null(recoveredQueued.ErrorMessage);
        Assert.Equal(nowUtc, recoveredQueued.LastCheckedAtUtc);

        var recoveredRunning = await LoadRefreshStateAsync(new DateTime(2026, 6, 2));
        Assert.Equal(SalesStatisticRefreshStatus.Pending, recoveredRunning!.Status);
        Assert.Null(recoveredRunning.JobId);
        Assert.Null(recoveredRunning.StartedAtUtc);
        Assert.Null(recoveredRunning.CompletedAtUtc);
        Assert.Null(recoveredRunning.ErrorMessage);
        Assert.Equal(nowUtc, recoveredRunning.LastCheckedAtUtc);

        Assert.Equal(SalesStatisticRefreshStatus.Queued, (await LoadRefreshStateAsync(new DateTime(2026, 6, 3)))!.Status);
        Assert.Equal(SalesStatisticRefreshStatus.Running, (await LoadRefreshStateAsync(new DateTime(2026, 6, 4)))!.Status);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, (await LoadRefreshStateAsync(new DateTime(2026, 6, 5)))!.Status);
        Assert.Equal(SalesStatisticRefreshStatus.Failed, (await LoadRefreshStateAsync(new DateTime(2026, 6, 6)))!.Status);
        Assert.Equal(SalesStatisticRefreshStatus.Stale, (await LoadRefreshStateAsync(new DateTime(2026, 6, 7)))!.Status);
        Assert.Equal(SalesStatisticRefreshStatus.Pending, (await LoadRefreshStateAsync(new DateTime(2026, 6, 8)))!.Status);
    }

    [Fact]
    public async Task RecoverTimedOutProductStoreDailyRecalculationJobsAsync_恢复后可再次提交重算()
    {
        var targetDate = new DateTime(2026, 6, 1);
        var nowUtc = new DateTime(2026, 6, 8, 6, 0, 0, DateTimeKind.Utc);
        var service = CreateService();
        await SeedRefreshStateAsync(
            targetDate,
            SalesStatisticRefreshStatus.Running,
            requestedAtUtc: nowUtc.AddHours(-1),
            startedAtUtc: nowUtc.AddMinutes(-31),
            jobId: Guid.NewGuid()
        );

        var recoveredCount = await service.RecoverTimedOutProductStoreDailyRecalculationJobsAsync(
            TimeSpan.FromMinutes(30),
            nowUtc
        );
        var result = await service.SubmitProductStoreDailyRecalculationAsync(new[] { targetDate }, "admin");

        Assert.Equal(1, recoveredCount);
        Assert.Single(result.SubmittedDates);
        Assert.Empty(result.SkippedDates);
        Assert.Equal(targetDate, result.SubmittedDates.Single());
    }

    [Fact]
    public async Task SubmitProductStoreDailyRecalculationAsync_重复日期与执行中日期仍按唯一日期跳过()
    {
        var queuedDate = new DateTime(2026, 6, 1);
        var freshDate = new DateTime(2026, 6, 2);
        var service = CreateService();
        await SeedRefreshStateAsync(
            queuedDate,
            SalesStatisticRefreshStatus.Running,
            requestedAtUtc: new DateTime(2026, 6, 8, 6, 0, 0, DateTimeKind.Utc),
            startedAtUtc: new DateTime(2026, 6, 8, 6, 1, 0, DateTimeKind.Utc),
            jobId: Guid.NewGuid()
        );

        var result = await service.SubmitProductStoreDailyRecalculationAsync(
            new[] { queuedDate, queuedDate, freshDate, freshDate },
            "admin",
            4
        );

        Assert.Equal(new[] { freshDate }, result.SubmittedDates);
        Assert.Equal(new[] { queuedDate }, result.SkippedDates);
    }

    [Fact]
    public void SubmitProductStoreDailyRecalculationAsync_保留默认并发参数并提供夹取帮助方法()
    {
        var submitMethod = typeof(SalesStatisticsJobService).GetMethod(
            nameof(SalesStatisticsJobService.SubmitProductStoreDailyRecalculationAsync)
        );
        Assert.NotNull(submitMethod);

        var parameters = submitMethod!.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal("maxConcurrency", parameters[2].Name);
        Assert.True(parameters[2].IsOptional);
        Assert.Equal(3, Assert.IsType<int>(parameters[2].DefaultValue));

        var clampMethod = typeof(SalesStatisticsJobService).GetMethod(
            "NormalizeProductStatisticMaxConcurrency",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.NotNull(clampMethod);
        Assert.Equal(3, Assert.IsType<int>(clampMethod!.Invoke(null, new object[] { 0 })));
        Assert.Equal(4, Assert.IsType<int>(clampMethod.Invoke(null, new object[] { 4 })));
        Assert.Equal(10, Assert.IsType<int>(clampMethod.Invoke(null, new object[] { 11 })));
    }

    [Fact]
    public async Task ExecuteTransactionSafelyAsync_业务异常后回滚再失败时_应保留原始业务异常()
    {
        var logger = new TestLogger<SalesStatisticsJobService>();
        var helper = typeof(SalesStatisticsJobService).GetMethod(
            "ExecuteTransactionSafelyAsync",
            BindingFlags.Static | BindingFlags.NonPublic
        );

        Assert.NotNull(helper);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeHelperAsync(
                helper!,
                () => Task.CompletedTask,
                () => throw new InvalidOperationException("业务失败"),
                () => Task.CompletedTask,
                () => throw new InvalidOperationException("回滚失败"),
                logger,
                "分时统计"
            )
        );

        Assert.Equal("业务失败", error.Message);
        Assert.Contains(
            logger.Entries,
            entry =>
                entry.LogLevel == LogLevel.Error
                && entry.Message.Contains("回滚事务失败", StringComparison.Ordinal)
                && entry.Message.Contains("分时统计", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task ExecuteTransactionSafelyAsync_提交异常后回滚再失败时_应保留原始提交异常()
    {
        var logger = new TestLogger<SalesStatisticsJobService>();
        var helper = typeof(SalesStatisticsJobService).GetMethod(
            "ExecuteTransactionSafelyAsync",
            BindingFlags.Static | BindingFlags.NonPublic
        );

        Assert.NotNull(helper);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeHelperAsync(
                helper!,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => throw new InvalidOperationException("提交失败"),
                () => throw new InvalidOperationException("回滚失败"),
                logger,
                "分店统计"
            )
        );

        Assert.Equal("提交失败", error.Message);
        Assert.Contains(
            logger.Entries,
            entry =>
                entry.LogLevel == LogLevel.Error
                && entry.Message.Contains("回滚事务失败", StringComparison.Ordinal)
                && entry.Message.Contains("分店统计", StringComparison.Ordinal)
        );
    }

    private SalesStatisticsJobService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ScheduledTasks:MaxConcurrentUpdates"] = "2",
                ["ScheduledTasks:MaxDaysForConcurrentUpdate"] = "30",
                ["ScheduledTasks:MaxDaysPerChunk"] = "7",
            })
            .Build();

        return new SalesStatisticsJobService(
            CreatePosmSqlSugarContext(_posmDb),
            CreateSqlSugarContext(_localDb),
            NullLogger<SalesStatisticsJobService>.Instance,
            configuration,
            Mock.Of<IServiceScopeFactory>()
        );
    }

    private async Task SeedSaleAsync(
        string orderGuid,
        string detailGuid,
        string productCode,
        string? branchCode,
        DateTime orderTime,
        int quantity,
        decimal actualAmount,
        string? supplierCode
    )
    {
        await _posmDb.Insertable(new SalesOrder
        {
            OrderGuid = orderGuid,
            BranchCode = branchCode,
            DeviceCode = null,
            OrderTime = orderTime,
            Status = 1,
            LastUploadTime = orderTime.AddMinutes(5),
        }).ExecuteCommandAsync();

        await _posmDb.Insertable(new SalesOrderDetail
        {
            OrderDetailGuid = detailGuid,
            OrderGuid = orderGuid,
            ProductCode = productCode,
            SupplierCode = supplierCode ?? string.Empty,
            ProductName = productCode,
            Barcode = $"{productCode}-BAR",
            Quantity = quantity,
            ActualAmount = actualAmount,
            LastUploadTime = orderTime.AddMinutes(6),
        }).ExecuteCommandAsync();
    }

    private async Task SeedProductAsync(string productCode)
    {
        await _localDb.Insertable(new Product
        {
            UUID = $"{productCode}-uuid",
            ProductCode = productCode,
            ItemNumber = productCode,
            Barcode = $"{productCode}-BAR",
            ProductName = productCode,
            LocalSupplierCode = "200",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new WarehouseProduct
        {
            ProductCode = productCode,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreSalesStatisticAsync(
        DateTime date,
        string branchCode,
        decimal totalAmount,
        int totalQuantity
    )
    {
        await _localDb.Insertable(new StoreSalesStatistic
        {
            Date = date.Date,
            BranchCode = branchCode,
            BranchName = $"Store-{branchCode}",
            TotalAmount = totalAmount,
            TotalQuantity = totalQuantity,
            OrderCount = 1,
            CustomerCount = 1,
            AverageOrderValue = totalAmount,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreSupplierSalesDetailAsync(
        DateTime date,
        string branchCode,
        string supplierCode,
        decimal totalAmount,
        int totalQuantity
    )
    {
        await _localDb.Insertable(new StoreSupplierSalesDetail
        {
            Date = date.Date,
            BranchCode = branchCode,
            SupplierCode = supplierCode,
            SupplierName = supplierCode,
            TotalAmount = totalAmount,
            TotalQuantity = totalQuantity,
            OrderCount = 1,
        }).ExecuteCommandAsync();
    }

    private async Task<SalesStatisticRefreshState?> LoadRefreshStateAsync(DateTime targetDate)
    {
        return await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(s =>
                s.StatisticType == SalesStatisticType.ProductStoreDaily
                && s.Date >= targetDate.Date
                && s.Date < targetDate.Date.AddDays(1)
            )
            .FirstAsync();
    }

    private async Task SeedRefreshStateAsync(
        DateTime date,
        string status,
        DateTime? requestedAtUtc = null,
        DateTime? startedAtUtc = null,
        DateTime? lastCheckedAtUtc = null,
        Guid? jobId = null,
        string? errorMessage = null
    )
    {
        await _localDb.Insertable(new SalesStatisticRefreshState
        {
            StatisticType = SalesStatisticType.ProductStoreDaily,
            Date = date.Date,
            Status = status,
            SourceTimeZone = "POSM_LOCAL",
            JobId = jobId,
            RequestedBy = jobId == null ? null : "admin",
            RequestedAtUtc = requestedAtUtc,
            StartedAtUtc = startedAtUtc,
            LastCheckedAtUtc = lastCheckedAtUtc ?? requestedAtUtc ?? startedAtUtc,
            ErrorMessage = errorMessage,
        }).ExecuteCommandAsync();
    }

    private static async Task InvokeHelperAsync(
        MethodInfo helper,
        Func<Task> beginAsync,
        Func<Task> workAsync,
        Func<Task> commitAsync,
        Func<Task> rollbackAsync,
        ILogger<SalesStatisticsJobService> logger,
        string operationName
    )
    {
        var task = helper.Invoke(
            null,
            new object[] { beginAsync, workAsync, commitAsync, rollbackAsync, logger, operationName }
        ) as Task;

        Assert.NotNull(task);
        await task!;
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
        if (File.Exists(_localDbPath))
            File.Delete(_localDbPath);
        if (File.Exists(_posmDbPath))
            File.Delete(_posmDbPath);
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message, Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose() { }
    }
}
