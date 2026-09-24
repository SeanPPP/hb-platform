using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;
using HbTaskStatus = BlazorApp.Shared.Models.HBweb.TaskStatus;

namespace BlazorApp.Api.Tests;

/// <summary>
/// HQ 增量同步（Sync*FromHqIncrementalAsync）未指定起始日期时的起点。
/// 这些方法曾按“上次成功时间”整天截断计算窗口，距上次成功不足 1 天时窗口为 0 天；
/// 只因先写自己的运行中日志、再读最近 1 条日志，才一直恒走 30 天兜底。
/// </summary>
public sealed class DataSyncIncrementalHqStartTests : IDisposable
{
    private static readonly TimeSpan Lookback = TimeSpan.FromDays(30);

    private readonly string _localDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqlSugarClient _localDb;

    public DataSyncIncrementalHqStartTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _localConnection.Open();
        _localDb = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = _localConnection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        CreateScheduledTaskLogTable(_localDb);
    }

    // 方法名、任务类型、是否按业务日期（SQL date）过滤；按主表 GUID 或全量同步的两个方法不在其列。
    public static TheoryData<string, string, bool> TimeFilteredHqIncrementalMethods =>
        new()
        {
            { nameof(DataSyncIncrementalService.SyncStoreLocalSupplierInvoicesFromHqIncrementalAsync), "SyncStoreLocalSupplierInvoicesIncremental", true },
            { nameof(DataSyncIncrementalService.SyncContainersFromHqIncrementalAsync), "SyncContainersIncremental", true },
            { nameof(DataSyncIncrementalService.SyncWareHouseOrdersFromHqIncrementalAsync), "SyncWareHouseOrdersIncremental", true },
            { nameof(DataSyncIncrementalService.SyncProductsFromHqIncrementalAsync), "SyncProductsIncremental", false },
            { nameof(DataSyncIncrementalService.SyncDomesticProductsFromHqIncrementalAsync), "SyncDomesticProductsIncremental", false },
            { nameof(DataSyncIncrementalService.SyncChinaSuppliersFromHqIncrementalAsync), "SyncChinaSuppliersIncremental", false },
            { nameof(DataSyncIncrementalService.SyncWarehouseCategoriesFromHqIncrementalAsync), "SyncWarehouseCategoriesIncremental", false },
            { nameof(DataSyncIncrementalService.SyncWarehouseProductsFromHqIncrementalAsync), "SyncWarehouseProductsIncremental", false },
            { nameof(DataSyncIncrementalService.SyncLocationsFromHqIncrementalAsync), "SyncLocationsIncremental", false },
            { nameof(DataSyncIncrementalService.SyncProductLocationsFromHqIncrementalAsync), "SyncProductLocationsIncremental", false },
            { nameof(DataSyncIncrementalService.SyncCashRegisterUsersFromHqIncrementalAsync), "SyncCashRegisterUsersIncremental", false },
            { nameof(DataSyncIncrementalService.SyncStoreMultiCodeProductsFromHqIncrementalAsync), "SyncStoreMultiCodeProductsIncremental", false },
            { nameof(DataSyncIncrementalService.SyncProductSetCodesFromHqIncrementalAsync), "SyncProductSetCodesIncremental", false },
            { nameof(DataSyncIncrementalService.SyncStoreClearancePricesFromHqIncrementalAsync), "SyncStoreClearancePricesIncremental", false },
            { nameof(DataSyncIncrementalService.SyncProductPrefixCodesFromHqIncrementalAsync), "SyncProductPrefixCodesIncremental", false },
            { nameof(DataSyncIncrementalService.SyncStoreLocalSupplierInvoiceDetailsFromHqIncrementalAsync), "SyncStoreLocalSupplierInvoiceDetailsIncremental", false },
            { nameof(DataSyncIncrementalService.SyncWareHouseOrderDetailsFromHqIncrementalAsync), "SyncWareHouseOrderDetailsIncremental", false },
            { nameof(DataSyncIncrementalService.SyncProductCategoriesFromHqIncrementalAsync), "SyncProductCategoriesIncremental", false },
        };

    [Theory]
    [MemberData(nameof(TimeFilteredHqIncrementalMethods))]
    public async Task HQ增量方法_上次成功就在20分钟前且本次任务日志未能写入时仍回溯30天而不是0天(
        string methodName,
        string taskType,
        bool filtersByBusinessDate
    )
    {
        await SeedSuccessTaskLogAsync(taskType, DateTime.UtcNow.AddMinutes(-20));
        // 复现 LogTaskStartAsync 写库失败、返回未持久化临时日志的情形：此时“读最近 1 条任务日志”
        // 拿到的正是上次成功记录，旧实现按整天截断得到 0 天，起点等于当前时间。
        BlockTaskLogInserts();
        var logger = new StartCapturingLogger<DataSyncIncrementalService>();
        var method = typeof(DataSyncIncrementalService).GetMethod(methodName)!;

        var before = DateTime.UtcNow;
        // 单元测试不连 HQ/HBSales：起点算完后会因连接未配置而失败返回，这里只校验起点。
        var invocation = (Task<SyncResult>)method.Invoke(
            CreateService(logger),
            new object?[method.GetParameters().Length]
        )!;
        await invocation;
        var after = DateTime.UtcNow;

        var start = Assert.Single(logger.StartValues);
        if (filtersByBusinessDate)
        {
            Assert.Contains(start, new[] { before.Date - Lookback, after.Date - Lookback });
        }
        else
        {
            Assert.InRange(start, before - Lookback, after - Lookback);
        }
    }

    [Fact]
    public void ResolveHqModifiedTimeIncrementalStart_固定回溯30天且保持UTC()
    {
        var utcNow = new DateTime(2026, 9, 21, 6, 6, 50, DateTimeKind.Utc);

        var start = DataSyncIncrementalService.ResolveHqModifiedTimeIncrementalStart(utcNow);

        Assert.Equal(new DateTime(2026, 8, 22, 6, 6, 50, DateTimeKind.Utc), start);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
        Assert.Equal(Lookback, DataSyncIncrementalService.HqIncrementalLookback);
    }

    [Theory]
    // 悉尼 9 月 21 日 16:06（AEST），UTC 与悉尼同为 9 月 21 日。
    [InlineData("2026-09-21T06:06:50Z", "2026-08-22")]
    // 悉尼 9 月 21 日 08:30（AEST），UTC 仍是 9 月 20 日：按 UTC 日期多回溯一天，悉尼 30 天仍全覆盖。
    [InlineData("2026-09-20T22:30:00Z", "2026-08-21")]
    // 悉尼夏令时 1 月 15 日 09:00（AEDT，UTC+11），UTC 仍是 1 月 14 日。
    [InlineData("2027-01-14T22:00:00Z", "2026-12-15")]
    public void ResolveHqBusinessDateIncrementalStart_从UTC当天零点回溯并覆盖悉尼最近30天(
        string utcNowText,
        string expectedStartDate
    )
    {
        var utcNow = DateTime.Parse(
            utcNowText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal
        );

        var start = DataSyncIncrementalService.ResolveHqBusinessDateIncrementalStart(utcNow);

        Assert.Equal(
            DateTime.ParseExact(expectedStartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            start
        );
        Assert.Equal(TimeSpan.Zero, start.TimeOfDay);
        // 业务日期列是悉尼日期，悉尼最多比 UTC 快 11 小时；起点不得晚于“悉尼今天 - 30 天”。
        Assert.True(start <= utcNow.AddHours(11).Date - Lookback);
    }

    public void Dispose()
    {
        _localDb.Dispose();
        _localConnection.Dispose();
        if (File.Exists(_localDbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        }
    }

    private DataSyncIncrementalService CreateService(ILogger<DataSyncIncrementalService> logger)
    {
        var localContext = CreateSqlSugarContext(_localDb);

        return new DataSyncIncrementalService(
            localContext,
            CreateContext<HqSqlSugarContext>(),
            CreateContext<HBSalesSqlSugarContext>(),
            CreateContext<POSMSqlSugarContext>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IMapper>(),
            logger,
            new ScheduledTaskLogService(localContext, NullLogger<ScheduledTaskLogService>.Instance),
            Mock.Of<IStoreRetailPriceHqSyncService>(),
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<IWarehouseProductChangeHistoryService>(),
            Mock.Of<ICurrentUserService>()
        );
    }

    private async Task SeedSuccessTaskLogAsync(string taskType, DateTime startedAt)
    {
        await _localDb.Insertable(
            new ScheduledTaskLog
            {
                Id = Guid.NewGuid(),
                TaskType = taskType,
                TaskParameters = "{}",
                Status = HbTaskStatus.Success,
                StartedAt = startedAt,
                CompletedAt = startedAt.AddSeconds(30),
                ScheduledTime = startedAt,
                TriggeredBy = TaskTrigger.Manual,
                RetryCount = 0,
                CanRetry = true,
                CreatedAt = startedAt,
                UpdatedAt = startedAt,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private void BlockTaskLogInserts()
    {
        _localDb.Ado.ExecuteCommand(
            """
            CREATE TRIGGER BlockScheduledTaskLogInsert BEFORE INSERT ON ScheduledTaskLog
            BEGIN
                SELECT RAISE(ABORT, 'simulated task log insert failure');
            END;
            """
        );
    }

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

    private static TContext CreateContext<TContext>()
        where TContext : class
    {
        return (TContext)RuntimeHelpers.GetUninitializedObject(typeof(TContext));
    }

    /// <summary>
    /// 只收集结构化日志里名为 Start 的参数，即各方法记录的增量起点。
    /// </summary>
    private sealed class StartCapturingLogger<T> : ILogger<T>
    {
        public List<DateTime> StartValues { get; } = new();

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
            if (state is not IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                return;
            }

            foreach (var pair in values)
            {
                if (pair.Key == "Start" && pair.Value is DateTime start)
                {
                    StartValues.Add(start);
                }
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose() { }
    }
}
