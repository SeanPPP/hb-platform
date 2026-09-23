using System.Runtime.CompilerServices;
using System.Reflection;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;
using HbTaskStatus = BlazorApp.Shared.Models.HBweb.TaskStatus;

namespace BlazorApp.Api.Tests;

public sealed class DataSyncIncrementalStoreRetailPriceTests : IDisposable
{
    private const string StoreRetailPricesIncrementalTaskType = "SyncStoreRetailPricesIncremental";
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public DataSyncIncrementalStoreRetailPriceTests()
    {
        _connection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _db = new SqlSugarClient(CreateConnectionConfig(_connection.ConnectionString));
        CreateScheduledTaskLogTable(_db);
    }

    [Fact]
    public async Task SyncStoreRetailPricesFromHqIncrementalAsync_全分店未指定日期时_从上次全分店水位回退十分钟续跑()
    {
        var watermarkStartedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        await SeedTaskLogAsync(
            watermarkStartedAt,
            HbTaskStatus.Success,
            StoreRetailPriceIncrementalTaskScope.BuildWatermarkParameters(watermarkStartedAt.AddDays(-1))
        );
        for (var i = 1; i <= 11; i++)
        {
            await SeedTaskLogAsync(
                watermarkStartedAt.AddHours(i),
                HbTaskStatus.Failed,
                StoreRetailPriceIncrementalTaskScope.BuildWatermarkParameters(watermarkStartedAt)
            );
        }
        var (hqSyncService, captured) = CreateCapturingHqSyncService();
        var service = CreateService(hqSyncService.Object);

        var result = await service.SyncStoreRetailPricesFromHqIncrementalAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("Watermark", captured.Entry);
        Assert.Equal(watermarkStartedAt.AddMinutes(-10), captured.StartDate);
    }

    [Fact]
    public async Task SyncStoreRetailPricesFromHqIncrementalAsync_页面局部同步和指定分店的成功记录_不污染全分店水位()
    {
        var watermarkStartedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        await SeedTaskLogAsync(
            watermarkStartedAt,
            HbTaskStatus.Success,
            StoreRetailPriceIncrementalTaskScope.BuildWatermarkParameters(null)
        );
        // 页面对 S01 同步了一段历史日期；旧接口又单独跑过 S02；还有一次全分店但指定起始日期的运行。
        await SeedTaskLogAsync(
            watermarkStartedAt.AddDays(3),
            HbTaskStatus.Success,
            StoreRetailPriceIncrementalTaskScope.BuildScopedParameters(
                new List<string> { "S01" },
                new DateTime(2026, 5, 1),
                new DateTime(2026, 5, 2)
            )
        );
        await SeedTaskLogAsync(
            watermarkStartedAt.AddDays(4),
            HbTaskStatus.Success,
            StoreRetailPriceIncrementalTaskScope.BuildScopedParameters(
                new List<string> { "S02" },
                watermarkStartedAt,
                null
            )
        );
        await SeedTaskLogAsync(
            watermarkStartedAt.AddDays(5),
            HbTaskStatus.Success,
            StoreRetailPriceIncrementalTaskScope.BuildScopedParameters(
                null,
                watermarkStartedAt.AddDays(4),
                null
            )
        );
        var (hqSyncService, captured) = CreateCapturingHqSyncService();
        var service = CreateService(hqSyncService.Object);

        var result = await service.SyncStoreRetailPricesFromHqIncrementalAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("Watermark", captured.Entry);
        Assert.Equal(watermarkStartedAt.AddMinutes(-10), captured.StartDate);
    }

    [Fact]
    public async Task SyncStoreRetailPricesFromHqIncrementalAsync_指定分店时_从全分店水位起步但走局部入口()
    {
        var watermarkStartedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        await SeedTaskLogAsync(
            watermarkStartedAt,
            HbTaskStatus.Success,
            StoreRetailPriceIncrementalTaskScope.BuildWatermarkParameters(null)
        );
        var (hqSyncService, captured) = CreateCapturingHqSyncService();
        var service = CreateService(hqSyncService.Object);

        var result = await service.SyncStoreRetailPricesFromHqIncrementalAsync(
            new List<string> { "S01" }
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Scoped", captured.Entry);
        Assert.Equal(new List<string> { "S01" }, captured.StoreCodes);
        Assert.Equal(watermarkStartedAt.AddMinutes(-10), captured.StartDate);
    }

    [Fact]
    public async Task SyncStoreRetailPricesFromHqIncrementalAsync_全分店但指定起始日期_走局部入口不推进水位()
    {
        var requestedStart = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
        var (hqSyncService, captured) = CreateCapturingHqSyncService();
        var service = CreateService(hqSyncService.Object);

        // 空白分店编码等同于未选择分店。
        var result = await service.SyncStoreRetailPricesFromHqIncrementalAsync(
            new List<string> { " " },
            requestedStart
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Scoped", captured.Entry);
        Assert.Equal(requestedStart, captured.StartDate);
    }

    [Fact]
    public async Task SyncStoreRetailPricesFromHqIncrementalAsync_没有历史成功任务时_保留统一服务默认窗口()
    {
        await SeedTaskLogAsync(DateTime.UtcNow.AddHours(-1), HbTaskStatus.Failed);
        var (hqSyncService, captured) = CreateCapturingHqSyncService();
        captured.StartDate = new DateTime(2026, 1, 1);
        var service = CreateService(hqSyncService.Object);

        var result = await service.SyncStoreRetailPricesFromHqIncrementalAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("Watermark", captured.Entry);
        Assert.Null(captured.StartDate);
    }

    [Fact]
    public async Task SyncStoreRetailPricesFromHqIncrementalAsync_只有默认窗口内的旧格式成功记录_不信任它而用默认窗口()
    {
        // 旧格式日志分不清全分店还是页面局部同步，三天前这条不能当水位。
        await SeedTaskLogAsync(DateTime.UtcNow.AddDays(-3), HbTaskStatus.Success);
        var (hqSyncService, captured) = CreateCapturingHqSyncService();
        captured.StartDate = new DateTime(2026, 1, 1);
        var service = CreateService(hqSyncService.Object);

        var result = await service.SyncStoreRetailPricesFromHqIncrementalAsync();

        Assert.True(result.IsSuccess);
        Assert.Null(captured.StartDate);
    }

    [Fact]
    public async Task SyncStoreRetailPricesFromHqIncrementalAsync_旧格式成功记录早于默认窗口_向更早方向扩展窗口()
    {
        var legacyStartedAt = DateTime.UtcNow.AddDays(-60);
        await SeedTaskLogAsync(legacyStartedAt, HbTaskStatus.Success);
        // 之后的页面局部同步再多也不能把旧记录挤掉。
        for (var i = 1; i <= 8; i++)
        {
            await SeedTaskLogAsync(
                DateTime.UtcNow.AddDays(-i),
                HbTaskStatus.Success,
                StoreRetailPriceIncrementalTaskScope.BuildScopedParameters(
                    new List<string> { "S01" },
                    new DateTime(2026, 5, 1),
                    new DateTime(2026, 5, 2)
                )
            );
        }
        var (hqSyncService, captured) = CreateCapturingHqSyncService();
        var service = CreateService(hqSyncService.Object);

        var result = await service.SyncStoreRetailPricesFromHqIncrementalAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured.StartDate);
        Assert.True(
            Math.Abs((captured.StartDate!.Value - legacyStartedAt.AddMinutes(-10)).TotalSeconds) < 1
        );
    }

    [Fact]
    public async Task SyncStoreRetailPricesFromHqIncrementalAsync_新水位记录优先于更新的旧格式记录()
    {
        var watermarkStartedAt = DateTime.UtcNow.AddDays(-90);
        await SeedTaskLogAsync(
            watermarkStartedAt,
            HbTaskStatus.Success,
            StoreRetailPriceIncrementalTaskScope.BuildWatermarkParameters(null)
        );
        await SeedTaskLogAsync(DateTime.UtcNow.AddDays(-80), HbTaskStatus.Success);
        var (hqSyncService, captured) = CreateCapturingHqSyncService();
        var service = CreateService(hqSyncService.Object);

        await service.SyncStoreRetailPricesFromHqIncrementalAsync();

        // 不设回溯上限：90 天前的水位原样生效。
        Assert.NotNull(captured.StartDate);
        Assert.True(
            Math.Abs((captured.StartDate!.Value - watermarkStartedAt.AddMinutes(-10)).TotalSeconds) < 1
        );
    }

    [Fact]
    public void StoreRetailPriceIncrementalTaskScope_标记与参数矛盾或缺失时_不可作为水位()
    {
        static ScheduledTaskLog Log(TaskParameters? parameters) =>
            new()
            {
                TaskParameters = parameters == null
                    ? null
                    : System.Text.Json.JsonSerializer.Serialize(parameters),
            };

        var eligible = StoreRetailPriceIncrementalTaskScope.BuildWatermarkParameters(DateTime.UtcNow);
        var contradictory = StoreRetailPriceIncrementalTaskScope.BuildWatermarkParameters(null);
        contradictory.BranchCodes = new List<string> { "S01" };

        Assert.True(StoreRetailPriceIncrementalTaskScope.IsWatermarkEligible(Log(eligible)));
        Assert.Contains(
            StoreRetailPriceIncrementalTaskScope.WatermarkEligibleJsonFragment,
            Log(eligible).TaskParameters
        );
        Assert.False(StoreRetailPriceIncrementalTaskScope.IsWatermarkEligible(Log(contradictory)));
        Assert.False(StoreRetailPriceIncrementalTaskScope.IsWatermarkEligible(Log(new TaskParameters())));
        Assert.False(StoreRetailPriceIncrementalTaskScope.IsWatermarkEligible(Log(null)));
        Assert.False(
            StoreRetailPriceIncrementalTaskScope.IsWatermarkEligible(
                Log(StoreRetailPriceIncrementalTaskScope.BuildScopedParameters(null, null, null))
            )
        );
        Assert.True(StoreRetailPriceIncrementalTaskScope.IsLegacyUnscoped(Log(new TaskParameters())));
        Assert.True(StoreRetailPriceIncrementalTaskScope.IsLegacyUnscoped(Log(null)));
        Assert.False(StoreRetailPriceIncrementalTaskScope.IsLegacyUnscoped(Log(eligible)));
    }

    [Fact]
    public void DataSyncIncrementalService_商品增量审计应在整轮事务结束后统一写入()
    {
        var source = File.ReadAllText(ResolveIncrementalServicePath());

        AssertAllProductIncrementalMethodsUseWholeTaskTransaction(source);
    }

    [Fact]
    public async Task DataSyncIncrementalService_actorName回退System但有用户Guid时仍保留用户身份()
    {
        WarehouseProductChangeHistoryContextDto? capturedContext = null;
        var historyService = new Mock<IWarehouseProductChangeHistoryService>(MockBehavior.Strict);
        historyService
            .Setup(service => service.RecordChangesAsync(
                It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                It.IsAny<WarehouseProductChangeHistoryContextDto>(),
                It.IsAny<CancellationToken>()
            ))
            .Callback<
                IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>,
                IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>,
                WarehouseProductChangeHistoryContextDto,
                CancellationToken
            >((_, _, context, _) => capturedContext = context)
            .ReturnsAsync(0);
        var currentUser = new Mock<ICurrentUserService>(MockBehavior.Strict);
        currentUser.Setup(service => service.GetCurrentUserGuid()).Returns("sync-user-guid");
        currentUser.Setup(service => service.GetCurrentUsername()).Returns("System");
        var service = CreateService(
            Mock.Of<IStoreRetailPriceHqSyncService>(),
            historyService.Object,
            currentUser.Object
        );

        await InvokeRecordChangeHistoryAsync(service);

        Assert.NotNull(capturedContext);
        Assert.Equal("sync-user-guid", capturedContext!.ActorUserGuid);
        Assert.Equal("User", capturedContext.ActorType);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class CapturedSyncCall
    {
        public string? Entry { get; set; }
        public List<string>? StoreCodes { get; set; }
        public DateTime? StartDate { get; set; }
    }

    private static (Mock<IStoreRetailPriceHqSyncService>, CapturedSyncCall) CreateCapturingHqSyncService()
    {
        var captured = new CapturedSyncCall();
        var hqSyncService = new Mock<IStoreRetailPriceHqSyncService>(MockBehavior.Strict);
        hqSyncService
            .Setup(service => service.SyncAllStoresFromWatermarkAsync(It.IsAny<DateTime?>()))
            .Callback<DateTime?>(startDate =>
            {
                captured.Entry = "Watermark";
                captured.StoreCodes = null;
                captured.StartDate = startDate;
            })
            .ReturnsAsync(new SyncResult { IsSuccess = true });
        hqSyncService
            .Setup(service => service.SyncIncrementalAsync(
                It.IsAny<List<string>?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>()
            ))
            .Callback<List<string>?, DateTime?, DateTime?>((storeCodes, startDate, _) =>
            {
                captured.Entry = "Scoped";
                captured.StoreCodes = storeCodes;
                captured.StartDate = startDate;
            })
            .ReturnsAsync(new SyncResult { IsSuccess = true });
        return (hqSyncService, captured);
    }

    private async Task SeedTaskLogAsync(
        DateTime startedAt,
        string status,
        TaskParameters? parameters = null
    )
    {
        await _db.Insertable(new ScheduledTaskLog
        {
            Id = Guid.NewGuid(),
            TaskType = StoreRetailPricesIncrementalTaskType,
            // 不传参数时模拟旧格式日志：全空参数的序列化结果。
            TaskParameters = System.Text.Json.JsonSerializer.Serialize(parameters ?? new TaskParameters()),
            Status = status,
            StartedAt = startedAt,
            CompletedAt = status == HbTaskStatus.Success ? startedAt.AddMinutes(5) : startedAt.AddMinutes(1),
            ScheduledTime = startedAt,
            TriggeredBy = TaskTrigger.Manual,
            RetryCount = 0,
            CanRetry = true,
            CreatedAt = startedAt,
            UpdatedAt = startedAt,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private DataSyncIncrementalService CreateService(
        IStoreRetailPriceHqSyncService hqSyncService,
        IWarehouseProductChangeHistoryService? historyService = null,
        ICurrentUserService? currentUserService = null
    )
    {
        var localContext = CreateSqlSugarContext(_db);
        return new DataSyncIncrementalService(
            localContext,
            CreateContext<HqSqlSugarContext>(),
            CreateContext<HBSalesSqlSugarContext>(),
            CreateContext<POSMSqlSugarContext>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IMapper>(),
            NullLogger<DataSyncIncrementalService>.Instance,
            new ScheduledTaskLogService(
                localContext,
                NullLogger<ScheduledTaskLogService>.Instance
            ),
            hqSyncService,
            new MemoryCache(new MemoryCacheOptions()),
            historyService ?? Mock.Of<IWarehouseProductChangeHistoryService>(),
            currentUserService ?? Mock.Of<ICurrentUserService>()
        );
    }

    private static void AssertAllProductIncrementalMethodsUseWholeTaskTransaction(string source)
    {
        foreach (var methodName in new[]
                 {
                     "SyncProductsFromHqIncrementalAsync",
                     "SyncDomesticProductsFromHqIncrementalAsync",
                     "SyncWarehouseProductsFromHqIncrementalAsync",
                 })
        {
            var method = ExtractMethod(source, methodName);
            var transactionStart = method.IndexOf("UseTranAsync", StringComparison.Ordinal);
            var pageLoopStart = method.IndexOf("for (var page = 1; page <= pages; page++)", StringComparison.Ordinal);

            Assert.True(transactionStart >= 0, $"{methodName} 缺少本地事务");
            Assert.True(
                transactionStart < pageLoopStart,
                $"{methodName} 仍在每页内开启事务，无法保证整轮原子性"
            );
            Assert.Equal(1, CountOccurrences(method, "RecordChangeHistoryAsync("));
            Assert.Equal(1, CountOccurrences(method, "var afterSnapshots ="));
            Assert.Contains(
                "Where(code => auditedProductCodes.Add(code))",
                method,
                StringComparison.Ordinal
            );
            Assert.Contains("StringComparer.OrdinalIgnoreCase", method, StringComparison.Ordinal);
            Assert.DoesNotContain("页{Page}出错", method, StringComparison.Ordinal);
            Assert.Contains("result.AddedCount = 0;", method, StringComparison.Ordinal);
            Assert.Contains("result.UpdatedCount = 0;", method, StringComparison.Ordinal);
        }
    }

    private static async Task InvokeRecordChangeHistoryAsync(DataSyncIncrementalService service)
    {
        var method = typeof(DataSyncIncrementalService).GetMethod(
            "RecordChangeHistoryAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        var task = (Task)method.Invoke(
            service,
            new object?[]
            {
                new Dictionary<string, WarehouseProductChangeSnapshotDto>(),
                new Dictionary<string, WarehouseProductChangeSnapshotDto>(),
                "Sync",
                "DataSyncIncremental",
                Guid.NewGuid(),
                DateTime.UtcNow,
            }
        )!;
        await task;
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var start = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"未找到 {methodName}");
        var openingBrace = source.IndexOf('{', start);
        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}' && --depth == 0)
                return source[start..(index + 1)];
        }

        throw new InvalidOperationException($"无法读取 {methodName} 方法体");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }

    private static string ResolveIncrementalServicePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "services/backend/BlazorApp.Api/Services/React/DataSyncIncrementalService.cs"
            );
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("未找到 DataSyncIncrementalService.cs");
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString) =>
        new()
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        };

    private static void CreateScheduledTaskLogTable(ISqlSugarClient db)
    {
        db.Ado.ExecuteCommand(
            """
            CREATE TABLE IF NOT EXISTS ScheduledTaskLog (
                Id TEXT PRIMARY KEY,
                TaskType TEXT NOT NULL,
                TaskParameters TEXT NULL,
                Status TEXT NOT NULL,
                StartedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                DurationMs INTEGER NULL,
                ErrorMessage TEXT NULL,
                RetryCount INTEGER NOT NULL,
                CanRetry INTEGER NOT NULL,
                ScheduledTime TEXT NOT NULL,
                TriggeredBy TEXT NULL,
                CreatedAt TEXT NOT NULL,
                CreatedBy TEXT NULL,
                UpdatedAt TEXT NULL,
                UpdatedBy TEXT NULL,
                IsDeleted INTEGER NULL
            );
            """
        );
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static T CreateContext<T>()
        where T : class
    {
        return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }
}
