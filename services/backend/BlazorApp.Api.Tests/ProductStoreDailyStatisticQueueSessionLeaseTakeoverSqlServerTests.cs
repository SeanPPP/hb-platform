using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Api.Services.Performance;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>
/// 真实 SQL Server：完整刷新崩溃遗留的 sqlsess1/9999 标记，商品 worker 只能在取得同日期
/// Session applock（原 owner 会话已退出）时接管；原 owner 仍持锁时必须跳过并退回队列。
/// </summary>
public sealed class ProductStoreDailyStatisticQueueSessionLeaseTakeoverSqlServerTests
{
    private const string TaskType = "DailyStatisticsAlignmentFullRefresh";

    [SalesStatisticsDateExecutionGuardSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task DrainOnceAsync_完整刷新会话已退出_接管Sqlsess1租约并执行()
    {
        await using var fixture = await Fixture.CreateAsync();
        var date = new DateTime(2026, 9, 14);
        await fixture.LeaveCrashedSessionLeaseAsync(date, killOwner: true);
        var crashedLease = await fixture.ReadLeaseAsync(date);
        var executor = new FreshMarkingExecutor(fixture);
        var queue = fixture.CreateQueue(executor);
        await queue.EnqueueAsync(new[] { date }, "admin");

        var processed = await queue.DrainOnceAsync();

        Assert.Equal(1, processed);
        Assert.Equal(1, executor.CallCount);
        var state = await fixture.ReadStateAsync(date);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state.Status);
        var lease = await fixture.ReadLeaseAsync(date);
        Assert.Equal(ScheduledTaskLeaseStatus.Success, lease.Status);
        Assert.NotEqual(crashedLease.LeaseToken, lease.LeaseToken);
        Assert.False(lease.LeaseToken!.StartsWith(
            SalesStatisticsDateExecutionGuard.SessionLeaseTokenPrefix,
            StringComparison.Ordinal));
        Assert.Null(lease.LeaseUntilUtc);
    }

    [SalesStatisticsDateExecutionGuardSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task DrainOnceAsync_完整刷新仍持有会话锁_不接管并退回Queued()
    {
        await using var fixture = await Fixture.CreateAsync();
        var date = new DateTime(2026, 9, 15);
        await using var owner = await fixture.HoldSessionLeaseAsync(date);
        var ownerLease = await fixture.ReadLeaseAsync(date);
        var executor = new FreshMarkingExecutor(fixture);
        var queue = fixture.CreateQueue(executor);
        await queue.EnqueueAsync(new[] { date }, "admin");

        var processed = await queue.DrainOnceAsync();

        Assert.Equal(0, processed);
        Assert.Equal(0, executor.CallCount);
        var state = await fixture.ReadStateAsync(date);
        Assert.Equal(SalesStatisticRefreshStatus.Queued, state.Status);
        var lease = await fixture.ReadLeaseAsync(date);
        Assert.Equal(ScheduledTaskLeaseStatus.Running, lease.Status);
        Assert.Equal(ownerLease.LeaseToken, lease.LeaseToken);
    }

    private sealed class FreshMarkingExecutor : IProductStoreDailyStatisticExecutor
    {
        private readonly Fixture _fixture;

        public FreshMarkingExecutor(Fixture fixture) => _fixture = fixture;

        public int CallCount { get; private set; }

        public async Task ExecuteQueuedDateAsync(
            DateTime date,
            Guid expectedJobId,
            Func<Task> validateExecutionOwnershipAsync,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            await validateExecutionOwnershipAsync();
            await _fixture.MarkFreshAsync(date, expectedJobId);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private const string ConnectionEnvironmentVariable =
            "SALES_STATISTICS_RECOVERY_SQLSERVER_TEST_CONNECTION";
        private readonly string _adminConnection;
        private readonly string _databaseName = "HbProductTakeover_" + Guid.NewGuid().ToString("N");
        private readonly List<ISqlSugarClient> _clients = new();
        private readonly List<ServiceProvider> _providers = new();
        private string _databaseConnection = string.Empty;

        private Fixture(string adminConnection) => _adminConnection = adminConnection;

        internal static async Task<Fixture> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
            Assert.False(string.IsNullOrWhiteSpace(configured));
            var configuredBuilder = new SqlConnectionStringBuilder(configured);
            // 与日期会话锁测试同一护栏：只允许本机测试实例，杜绝误连生产库。
            Assert.Contains(
                configuredBuilder.DataSource,
                new[] { "127.0.0.1,15438", "localhost,15438" },
                StringComparer.OrdinalIgnoreCase
            );

            var fixture = new Fixture(configured!);
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(configured)
            {
                InitialCatalog = "master",
            }.ConnectionString);
            await master.OpenAsync();
            await using (var command = master.CreateCommand())
            {
                command.CommandText = $"CREATE DATABASE [{fixture._databaseName}]";
                await command.ExecuteNonQueryAsync();
            }

            fixture._databaseConnection = new SqlConnectionStringBuilder(configured)
            {
                InitialCatalog = fixture._databaseName,
                ConnectRetryCount = 0,
            }.ConnectionString;
            try
            {
                using var setup = fixture.OpenDatabase();
                setup.CodeFirst.InitTables(
                    typeof(ScheduledTaskLease),
                    typeof(SalesStatisticRefreshState),
                    typeof(ScheduledTaskLog)
                );
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        internal ProductStoreDailyStatisticQueueService CreateQueue(IProductStoreDailyStatisticExecutor executor)
        {
            var services = new ServiceCollection();
            // 与生产一致：每个 scope 一个独立 SqlSugarClient，接管用的 guard 只固定自己那条连接。
            services.AddScoped(_ => CreateContext(TrackClient(OpenDatabase())));
            services.AddScoped(provider => new ScheduledTaskLeaseService(
                provider.GetRequiredService<SqlSugarContext>(),
                Options.Create(new ScheduledTaskOptions { InstanceId = "product-worker" }),
                NullLogger<ScheduledTaskLeaseService>.Instance
            ));
            services.AddSingleton(executor);
            services.AddSingleton(new Mock<ISalesDashboardCacheWarmer>().Object);
            var provider = services.BuildServiceProvider();
            _providers.Add(provider);

            var context = CreateContext(TrackClient(OpenDatabase()));
            return new ProductStoreDailyStatisticQueueService(
                context,
                new ScheduledTaskLogService(context, NullLogger<ScheduledTaskLogService>.Instance),
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ProductStoreDailyStatisticQueueService>.Instance
            );
        }

        /// <summary>模拟完整刷新写下 sqlsess1 标记后进程被杀：会话退出，租约行停在 Running + 9999。</summary>
        internal async Task LeaveCrashedSessionLeaseAsync(DateTime date, bool killOwner)
        {
            var owner = await HoldSessionLeaseAsync(date);
            if (killOwner)
            {
                await KillAsync(owner.Guard.ServerProcessIdForTest);
            }
            await owner.DisposeAsync();
        }

        internal async Task<SessionOwner> HoldSessionLeaseAsync(DateTime date)
        {
            var db = TrackClient(OpenDatabase());
            var context = CreateContext(db);
            var leaseService = new ScheduledTaskLeaseService(
                context,
                Options.Create(new ScheduledTaskOptions { InstanceId = "full-refresh" }),
                NullLogger<ScheduledTaskLeaseService>.Instance
            );
            var guard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
                context,
                date,
                NullLogger.Instance
            );
            Assert.True(guard.Acquired);
            var lease = await leaseService.TryAcquireSessionGuardedAsync(
                TaskType,
                date.ToString("yyyy-MM-dd"),
                guard
            );
            Assert.True(lease.Acquired);
            return new SessionOwner(guard);
        }

        internal async Task MarkFreshAsync(DateTime date, Guid jobId)
        {
            using var db = OpenDatabase();
            var updated = await db.Updateable<SalesStatisticRefreshState>()
                .SetColumns(x => x.Status == SalesStatisticRefreshStatus.Fresh)
                .SetColumns(x => x.CompletedAtUtc == DateTime.UtcNow)
                .Where(x =>
                    x.StatisticType == SalesStatisticType.ProductStoreDaily
                    && x.Date == date
                    && x.JobId == jobId
                    && x.Status == SalesStatisticRefreshStatus.Running
                )
                .ExecuteCommandAsync();
            Assert.Equal(1, updated);
        }

        internal async Task<SalesStatisticRefreshState> ReadStateAsync(DateTime date)
        {
            using var db = OpenDatabase();
            return await db.Queryable<SalesStatisticRefreshState>()
                .SingleAsync(x => x.StatisticType == SalesStatisticType.ProductStoreDaily && x.Date == date);
        }

        internal async Task<ScheduledTaskLease> ReadLeaseAsync(DateTime date)
        {
            using var db = OpenDatabase();
            return await db.Queryable<ScheduledTaskLease>()
                .SingleAsync(row => row.TaskType == TaskType
                    && row.ScopeKey == date.ToString("yyyy-MM-dd"));
        }

        private async Task KillAsync(int serverProcessId)
        {
            await using var admin = new SqlConnection(new SqlConnectionStringBuilder(_adminConnection)
            {
                InitialCatalog = "master",
            }.ConnectionString);
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"KILL {serverProcessId}";
            await command.ExecuteNonQueryAsync();
        }

        private ISqlSugarClient TrackClient(ISqlSugarClient client)
        {
            lock (_clients)
            {
                _clients.Add(client);
            }
            return client;
        }

        private SqlSugarClient OpenDatabase() => new(new ConnectionConfig
        {
            ConnectionString = _databaseConnection,
            DbType = DbType.SqlServer,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        });

        public async ValueTask DisposeAsync()
        {
            foreach (var provider in _providers)
            {
                await provider.DisposeAsync();
            }
            foreach (var client in _clients)
            {
                try
                {
                    client.Dispose();
                }
                catch
                {
                    // 被 KILL 的会话在释放时可能再次报错，不影响清理测试库。
                }
            }
            if (string.IsNullOrWhiteSpace(_databaseConnection))
                return;
            SqlConnection.ClearAllPools();
            await using var admin = new SqlConnection(new SqlConnectionStringBuilder(_adminConnection)
            {
                InitialCatalog = "master",
            }.ConnectionString);
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]";
            await command.ExecuteNonQueryAsync();
        }

        private static SqlSugarContext CreateContext(ISqlSugarClient db)
        {
            var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
            typeof(SqlSugarContext)
                .GetField("_db", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(context, db);
            return context;
        }
    }

    private sealed class SessionOwner : IAsyncDisposable
    {
        internal SessionOwner(SalesStatisticsDateExecutionGuard guard) => Guard = guard;

        internal SalesStatisticsDateExecutionGuard Guard { get; }

        public ValueTask DisposeAsync() => Guard.DisposeAsync();
    }
}
