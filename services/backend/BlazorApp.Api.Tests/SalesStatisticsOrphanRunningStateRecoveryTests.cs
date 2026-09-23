using BlazorApp.Api.Services;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesStatisticsOrphanRunningStateRecoveryTests : IDisposable
{
    // 测试时间统一取整秒：SQLite 按文本比较日期，避免毫秒格式差异干扰 <= 边界。
    private static readonly DateTime NowUtc = new(2026, 9, 22, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OrphanDate = new(2026, 6, 29);

    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public SalesStatisticsOrphanRunningStateRecoveryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables<SalesStatisticRefreshState, ScheduledTaskLease>();
    }

    [Fact]
    public async Task 超过两小时的非商品Running且无租约_改为Failed并保留其余字段()
    {
        var lastAggregated = new DateTime(2026, 7, 8, 7, 45, 17);
        await InsertStateAsync(SalesStatisticType.SupplierSales, OrphanDate, NowUtc.AddDays(-76), lastAggregated);

        var recovered = await CreateRecovery().RecoverAsync(NowUtc, CancellationToken.None);

        Assert.Equal(1, recovered);
        var state = await GetStateAsync(SalesStatisticType.SupplierSales, OrphanDate);
        Assert.Equal(SalesStatisticRefreshStatus.Failed, state.Status);
        Assert.Equal(SalesStatisticsOrphanRunningStateRecovery.RecoveredErrorMessage, state.ErrorMessage);
        Assert.Equal(NowUtc, state.LastCheckedAtUtc);
        // 事实数据仍是上次成功的版本，完成时间与聚合水位不能被回收动作改写。
        Assert.Equal(lastAggregated, state.LastAggregatedAtUtc);
        Assert.Null(state.CompletedAtUtc);
    }

    [Fact]
    public async Task 未满两小时的Running_不回收()
    {
        await InsertStateAsync(SalesStatisticType.StoreSupplierSales, OrphanDate, NowUtc.AddMinutes(-119));

        var recovered = await CreateRecovery().RecoverAsync(NowUtc, CancellationToken.None);

        Assert.Equal(0, recovered);
        Assert.Equal(
            SalesStatisticRefreshStatus.Running,
            (await GetStateAsync(SalesStatisticType.StoreSupplierSales, OrphanDate)).Status);
    }

    [Fact]
    public async Task ProductStoreDaily超时Running_交给商品队列回收_这里不碰()
    {
        await InsertStateAsync(SalesStatisticType.ProductStoreDaily, OrphanDate, NowUtc.AddDays(-3));

        var recovered = await CreateRecovery().RecoverAsync(NowUtc, CancellationToken.None);

        Assert.Equal(0, recovered);
        Assert.Equal(
            SalesStatisticRefreshStatus.Running,
            (await GetStateAsync(SalesStatisticType.ProductStoreDaily, OrphanDate)).Status);
    }

    [Fact]
    public async Task 同日期TTL租约仍有效_视为有执行者不回收()
    {
        await InsertStateAsync(SalesStatisticType.DailySales, OrphanDate, NowUtc.AddHours(-3));
        await InsertLeaseAsync(OrphanDate, "legacy-ttl-token", NowUtc.AddMinutes(30));

        var recovered = await CreateRecovery().RecoverAsync(NowUtc, CancellationToken.None);

        Assert.Equal(0, recovered);
        Assert.Equal(
            SalesStatisticRefreshStatus.Running,
            (await GetStateAsync(SalesStatisticType.DailySales, OrphanDate)).Status);
    }

    [Fact]
    public async Task 崩溃遗留的session租约标记_不按TTL视为活跃()
    {
        await InsertStateAsync(SalesStatisticType.HourlySales, OrphanDate, NowUtc.AddHours(-3));
        await InsertLeaseAsync(
            OrphanDate,
            SalesStatisticsDateExecutionGuard.SessionLeaseTokenPrefix + Guid.NewGuid().ToString("N"),
            new DateTime(9999, 12, 31));

        var recovered = await CreateRecovery().RecoverAsync(NowUtc, CancellationToken.None);

        Assert.Equal(1, recovered);
        Assert.Equal(
            SalesStatisticRefreshStatus.Failed,
            (await GetStateAsync(SalesStatisticType.HourlySales, OrphanDate)).Status);
    }

    [Fact]
    public async Task 缺开始时间时按最后检查时间判断超时()
    {
        await _db.Insertable(new SalesStatisticRefreshState
        {
            StatisticType = SalesStatisticType.StoreSales,
            Date = OrphanDate,
            Status = SalesStatisticRefreshStatus.Running,
            LastCheckedAtUtc = NowUtc.AddHours(-5),
        }).ExecuteCommandAsync();

        var recovered = await CreateRecovery().RecoverAsync(NowUtc, CancellationToken.None);

        Assert.Equal(1, recovered);
        Assert.Equal(
            SalesStatisticRefreshStatus.Failed,
            (await GetStateAsync(SalesStatisticType.StoreSales, OrphanDate)).Status);
    }

    [Fact]
    public async Task 只回收超时行_同日其他类型与其他日期不受影响()
    {
        await InsertStateAsync(SalesStatisticType.SupplierSales, OrphanDate, NowUtc.AddHours(-3));
        await _db.Insertable(new SalesStatisticRefreshState
        {
            StatisticType = SalesStatisticType.DailySales,
            Date = OrphanDate,
            Status = SalesStatisticRefreshStatus.Fresh,
            StartedAtUtc = NowUtc.AddHours(-4),
            CompletedAtUtc = NowUtc.AddHours(-4),
        }).ExecuteCommandAsync();
        await InsertStateAsync(SalesStatisticType.SupplierSales, OrphanDate.AddDays(1), NowUtc.AddMinutes(-10));

        var recovered = await CreateRecovery().RecoverAsync(NowUtc, CancellationToken.None);

        Assert.Equal(1, recovered);
        Assert.Equal(
            SalesStatisticRefreshStatus.Fresh,
            (await GetStateAsync(SalesStatisticType.DailySales, OrphanDate)).Status);
        Assert.Equal(
            SalesStatisticRefreshStatus.Running,
            (await GetStateAsync(SalesStatisticType.SupplierSales, OrphanDate.AddDays(1))).Status);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    private SalesStatisticsOrphanRunningStateRecovery CreateRecovery() =>
        new(_db, NullLogger.Instance);

    private Task<int> InsertStateAsync(
        string statisticType,
        DateTime date,
        DateTime startedAtUtc,
        DateTime? lastAggregatedAtUtc = null) =>
        _db.Insertable(new SalesStatisticRefreshState
        {
            StatisticType = statisticType,
            Date = date,
            Status = SalesStatisticRefreshStatus.Running,
            StartedAtUtc = startedAtUtc,
            LastCheckedAtUtc = startedAtUtc,
            LastAggregatedAtUtc = lastAggregatedAtUtc,
        }).ExecuteCommandAsync();

    private Task<int> InsertLeaseAsync(DateTime date, string token, DateTime leaseUntilUtc) =>
        _db.Insertable(new ScheduledTaskLease
        {
            TaskType = SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType,
            ScopeKey = date.ToString("yyyy-MM-dd"),
            Status = ScheduledTaskLeaseStatus.Running,
            OwnerInstanceId = "other-instance",
            LeaseToken = token,
            LeaseUntilUtc = leaseUntilUtc,
            StartedAtUtc = NowUtc.AddHours(-3),
            UpdatedAtUtc = NowUtc.AddHours(-3),
        }).ExecuteCommandAsync();

    private Task<SalesStatisticRefreshState> GetStateAsync(string statisticType, DateTime date) =>
        _db.Queryable<SalesStatisticRefreshState>()
            .SingleAsync(state => state.StatisticType == statisticType && state.Date == date);
}

public sealed class SalesStatisticsOrphanRunningStateSqlServerFactAttribute : FactAttribute
{
    internal const string ConnectionEnvironmentVariable = "HB_TEST_SQLSERVER_CONNECTION";

    public SalesStatisticsOrphanRunningStateSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
        {
            Skip = $"未配置 {ConnectionEnvironmentVariable}，跳过孤儿统计状态回收的 SQL Server 会话锁验证。";
        }
    }
}

[Trait("Category", "SQL")]
public sealed class SalesStatisticsOrphanRunningStateRecoverySqlServerTests
{
    private static readonly DateTime OrphanDate = new(2026, 6, 29);

    [SalesStatisticsOrphanRunningStateSqlServerFact]
    public async Task 另一会话持有日期锁时不回收_释放后回收()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SalesStatisticsOrphanRunningStateSqlServerFactAttribute.ConnectionEnvironmentVariable)!;
        var databaseName = $"HbOrphanRunning_{Guid.NewGuid():N}";
        // 测试会建库删库，只允许指向本机隔离实例。
        var dataSource = new SqlConnectionStringBuilder(baseConnectionString).DataSource;
        Assert.True(
            dataSource.StartsWith("127.0.0.1", StringComparison.Ordinal)
                || dataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase),
            $"SQL Server 集成测试只允许回环地址: {dataSource}");
        var masterConnectionString = BuildConnectionString(baseConnectionString, "master");
        var databaseConnectionString = BuildConnectionString(baseConnectionString, databaseName);
        await ExecuteAsync(masterConnectionString, $"CREATE DATABASE [{databaseName}] COLLATE Chinese_PRC_90_CI_AS;");
        try
        {
            using var db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = databaseConnectionString,
                DbType = DbType.SqlServer,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            });
            db.CodeFirst.InitTables<SalesStatisticRefreshState, ScheduledTaskLease>();
            await ExecuteAsync(databaseConnectionString, """
                ALTER TABLE [SalesStatisticRefreshState] ALTER COLUMN [StartedAtUtc] datetime2(7) NULL;
                ALTER TABLE [SalesStatisticRefreshState] ALTER COLUMN [LastCheckedAtUtc] datetime2(7) NULL;
                """);
            // 生产上 StartedAtUtc 带 7 位小数（如 08:47:05.6666667），条件更新不能依赖等值比较。
            await ExecuteAsync(databaseConnectionString, """
                INSERT INTO [SalesStatisticRefreshState]
                    ([StatisticType], [Date], [Status], [SourceTimeZone], [StartedAtUtc], [LastCheckedAtUtc])
                VALUES (N'SupplierSales', '2026-06-29', N'Running', N'POSM_LOCAL',
                        DATEADD(DAY, -76, SYSUTCDATETIME()), DATEADD(DAY, -76, SYSUTCDATETIME()));
                """);
            var recovery = new SalesStatisticsOrphanRunningStateRecovery(db, NullLogger.Instance);

            // 持锁连接不能进连接池：池化连接关闭后要等复用时才重置，session 锁会残留。
            var holderConnectionString = new SqlConnectionStringBuilder(databaseConnectionString) { Pooling = false }.ConnectionString;
            await using (var holder = new SqlConnection(holderConnectionString))
            {
                await holder.OpenAsync();
                await using var acquire = holder.CreateCommand();
                acquire.CommandText =
                    "DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource=@resource, @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=0; SELECT @r;";
                acquire.Parameters.AddWithValue("@resource", SalesStatisticsDateExecutionGuard.GetLockResource(OrphanDate));
                Assert.True(Convert.ToInt32(await acquire.ExecuteScalarAsync()) >= 0);

                Assert.Equal(0, await recovery.RecoverAsync(DateTime.UtcNow, CancellationToken.None));
                Assert.Equal(SalesStatisticRefreshStatus.Running, await ReadStatusAsync(db));
            }

            // 持锁连接关闭即释放 session 锁，模拟执行者进程已消失。
            Assert.Equal(1, await recovery.RecoverAsync(DateTime.UtcNow, CancellationToken.None));
            Assert.Equal(SalesStatisticRefreshStatus.Failed, await ReadStatusAsync(db));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await ExecuteAsync(
                masterConnectionString,
                $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];");
        }
    }

    private static Task<string> ReadStatusAsync(SqlSugarClient db) =>
        db.Queryable<SalesStatisticRefreshState>()
            .Where(state => state.StatisticType == SalesStatisticType.SupplierSales)
            .Select(state => state.Status)
            .SingleAsync();

    private static string BuildConnectionString(string baseConnectionString, string databaseName) =>
        new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = databaseName }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
