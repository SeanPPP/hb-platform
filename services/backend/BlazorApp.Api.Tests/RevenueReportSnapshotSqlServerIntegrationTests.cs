using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class RevenueReportSnapshotSqlServerFactAttribute : FactAttribute
{
    private const string ConnectionEnvironmentVariable = "HB_TEST_SQLSERVER_CONNECTION";

    public RevenueReportSnapshotSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
        {
            Skip = $"未配置 {ConnectionEnvironmentVariable}，跳过真实 SQL Server 营业额快照验证。";
        }
    }
}

[Trait("Category", "SQL")]
public sealed class RevenueReportSnapshotSqlServerIntegrationTests
{
    private const string SqlServerTestConnectionEnvVar = "HB_TEST_SQLSERVER_CONNECTION";
    private static readonly DateTime SeedDate = new(2026, 9, 7);
    private static readonly DateTime EmptyDate = new(2026, 9, 8);
    private static readonly DateTime PublishedDate = new(2026, 9, 9);
    private static readonly DateTime LegacyDate = new(2026, 9, 10);
    private static readonly DateTime PublishedZeroDate = new(2026, 9, 11);
    private static readonly string[] AuthorizedStoreCodes =
    {
        "1003", "1004", "1005", "1009", "1012", "1014", "1015", "1024",
    };

    [RevenueReportSnapshotSqlServerFact]
    public async Task 店长显式八店范围只返回授权门店并保留中文小数金额()
    {
        await using var fixture = await RevenueSnapshotSqlServerFixture.CreateAsync();
        var result = await fixture.CreateService().GetRevenueReportSnapshotAsync(
            new DateRangeDto { StartDate = SeedDate, EndDate = SeedDate },
            AuthorizedStoreCodes.ToList(),
            AuthorizedStoreCodes.ToList());

        Assert.False(result.StatisticsPending);
        Assert.Equal(AuthorizedStoreCodes.Length, result.Branches.Count);
        Assert.Equal(
            AuthorizedStoreCodes.OrderBy(code => code),
            result.Branches.Select(row => row.BranchCode).OrderBy(code => code));
        Assert.DoesNotContain(result.Branches, row => row.BranchCode == "OUTSIDE");
        var store = Assert.Single(result.Branches, row => row.BranchCode == "1003");
        Assert.Equal("一〇〇三店", store.BranchName);
        Assert.Equal(100.13m, store.Revenue);
        Assert.Equal(3, store.OrderCount);
    }

    [RevenueReportSnapshotSqlServerFact]
    public async Task 空focus保留授权排行且分时和周层级为空()
    {
        await using var fixture = await RevenueSnapshotSqlServerFixture.CreateAsync();
        var result = await fixture.CreateService().GetRevenueReportSnapshotAsync(
            new DateRangeDto { StartDate = SeedDate, EndDate = SeedDate },
            AuthorizedStoreCodes.ToList(),
            new List<string>());

        Assert.False(result.StatisticsPending);
        Assert.Equal(AuthorizedStoreCodes.Length, result.Branches.Count);
        Assert.Equal(100.13m, Assert.Single(result.Branches, row => row.BranchCode == "1003").Revenue);
        Assert.Empty(result.Hourly);
        Assert.Empty(result.Weekly);
    }

    [RevenueReportSnapshotSqlServerFact]
    public async Task 空日期无统计数据时不抛错并标记Pending()
    {
        await using var fixture = await RevenueSnapshotSqlServerFixture.CreateAsync();
        var result = await fixture.CreateService().GetRevenueReportSnapshotAsync(
            new DateRangeDto { StartDate = EmptyDate, EndDate = EmptyDate },
            AuthorizedStoreCodes.ToList(),
            AuthorizedStoreCodes.ToList());

        Assert.True(result.StatisticsPending);
        Assert.True(result.CurrentPeriodPending);
        Assert.Equal(AuthorizedStoreCodes.Length, result.Branches.Count);
        Assert.All(result.Branches, row =>
        {
            Assert.Contains(row.BranchCode, AuthorizedStoreCodes);
            Assert.Equal(0m, row.Revenue);
        });
        Assert.Empty(result.Hourly);
        Assert.Empty(result.Weekly);
    }

    [RevenueReportSnapshotSqlServerFact]
    public async Task 管理员空范围读取活动门店目录并包含OUTSIDE()
    {
        await using var fixture = await RevenueSnapshotSqlServerFixture.CreateAsync();
        var result = await fixture.CreateService().GetRevenueReportSnapshotAsync(
            new DateRangeDto { StartDate = SeedDate, EndDate = SeedDate },
            null,
            null);

        Assert.False(result.StatisticsPending);
        Assert.Equal(9, result.Branches.Count);
        var outside = Assert.Single(result.Branches, row => row.BranchCode == "OUTSIDE");
        Assert.Equal("店外测试", outside.BranchName);
        Assert.Equal(999.99m, outside.Revenue);
    }

    [RevenueReportSnapshotSqlServerFact]
    public async Task 原生SQL读取按日期切换发布版本_真零覆盖旧行_旧BulkCopy不污染认证结果()
    {
        await using var fixture = await RevenueSnapshotSqlServerFixture.CreateAsync();
        await fixture.SeedPublicationScenarioAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = fixture.CreateService(cache);

        var mixed = await service.GetRevenueReportSnapshotAsync(
            new DateRangeDto { StartDate = PublishedDate, EndDate = LegacyDate },
            ["1003"],
            ["1003"]);

        Assert.False(mixed.StatisticsPending);
        var mixedHour = Assert.Single(mixed.Hourly);
        Assert.Equal("09:00", mixedHour.Hour);
        Assert.Equal("1003", mixedHour.BranchCode);
        Assert.Equal(30m, mixedHour.Revenue);
        Assert.Equal(3, mixedHour.OrderCount);
        Assert.Equal(2, await fixture.CountReadRowsAsync(PublishedDate, LegacyDate));

        var publishedBeforeBulk = await fixture.ReadViewAsync(PublishedDate);
        await fixture.LegacyFastestBulkCopyAsync(PublishedDate);
        Assert.Equal(2, await fixture.CountPhysicalRowsAsync(PublishedDate));
        Assert.Equal(
            publishedBeforeBulk.Select(RowIdentity),
            (await fixture.ReadViewAsync(PublishedDate)).Select(RowIdentity));

        using var refreshedCache = new MemoryCache(new MemoryCacheOptions());
        var afterBulk = await fixture.CreateService(refreshedCache).GetRevenueReportSnapshotAsync(
            new DateRangeDto { StartDate = PublishedDate, EndDate = LegacyDate },
            ["1003"],
            ["1003"]);
        var afterBulkHour = Assert.Single(afterBulk.Hourly);
        Assert.Equal(30m, afterBulkHour.Revenue);
        Assert.DoesNotContain(afterBulk.Hourly, row => row.Hour == "10:00");

        var zero = await fixture.CreateService(refreshedCache).GetRevenueReportSnapshotAsync(
            new DateRangeDto { StartDate = PublishedZeroDate, EndDate = PublishedZeroDate },
            ["1003"],
            ["1003"]);
        Assert.False(zero.StatisticsPending);
        Assert.False(zero.HourlyCurrentPending);
        Assert.Empty(zero.Hourly);
        Assert.Equal(70m, Assert.Single(await fixture.ReadPhysicalAsync(PublishedZeroDate)).TotalAmount);
        Assert.Empty(await fixture.ReadViewAsync(PublishedZeroDate));
    }

    [RevenueReportSnapshotSqlServerFact]
    public async Task 来源漂移的Applied版本抛Unavailable且不复用之前缓存()
    {
        await using var fixture = await RevenueSnapshotSqlServerFixture.CreateAsync();
        await fixture.SeedPublicationScenarioAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = fixture.CreateService(cache);
        var range = new DateRangeDto { StartDate = PublishedDate, EndDate = PublishedDate };

        var warm = await service.GetRevenueReportSnapshotAsync(range, ["1003"], ["1003"]);
        Assert.Equal(10m, Assert.Single(warm.Hourly).Revenue);

        await fixture.MarkPublicationSourceDriftAsync(PublishedDate);
        var error = await Assert.ThrowsAsync<HourlySalesPublicationUnavailableException>(
            () => service.GetRevenueReportSnapshotAsync(range, ["1003"], ["1003"]));
        Assert.Contains("来源复核未通过", error.Message);
        Assert.Empty(await fixture.ReadViewAsync(PublishedDate));
    }

    [RevenueReportSnapshotSqlServerFact]
    public async Task 未知RuleVersion的Applied版本抛Unavailable且不回退原表()
    {
        await using var fixture = await RevenueSnapshotSqlServerFixture.CreateAsync();
        await fixture.SeedPublicationScenarioAsync();
        await fixture.MarkPublicationRuleUnsupportedAsync(PublishedDate);
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var error = await Assert.ThrowsAsync<HourlySalesPublicationUnavailableException>(() =>
            fixture.CreateService(cache).GetRevenueReportSnapshotAsync(
                new DateRangeDto { StartDate = PublishedDate, EndDate = PublishedDate },
                ["1003"],
                ["1003"]));

        Assert.Contains("来源复核未通过", error.Message);
        Assert.Single(await fixture.ReadPhysicalAsync(PublishedDate));
        Assert.Empty(await fixture.ReadViewAsync(PublishedDate));
    }

    private static string RowIdentity(HourlySalesStatistic row) =>
        $"{row.Date:yyyy-MM-dd}|{row.Hour}|{row.BranchCode}|{row.TotalAmount}|{row.OrderCount}";

    private sealed class RevenueSnapshotSqlServerFixture : IAsyncDisposable
    {
        private readonly string _masterConnectionString;
        private readonly string _databaseName;
        private readonly SqlSugarClient _db;
        private readonly SqlSugarClient _posmDb;
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());

        private RevenueSnapshotSqlServerFixture(
            string masterConnectionString,
            string databaseName,
            string databaseConnectionString)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            _db = new SqlSugarClient(CreateConnectionConfig(databaseConnectionString));
            _posmDb = new SqlSugarClient(CreateConnectionConfig(databaseConnectionString));
        }

        public static async Task<RevenueSnapshotSqlServerFixture> CreateAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerTestConnectionEnvVar);
            if (string.IsNullOrWhiteSpace(baseConnectionString))
                throw new InvalidOperationException($"未配置 {SqlServerTestConnectionEnvVar}。");
            EnsureLoopbackSqlServer(baseConnectionString);

            var databaseName = $"HbRevenueSnapshot_{Guid.NewGuid():N}";
            var masterConnectionString = BuildConnectionString(baseConnectionString, "master");
            var databaseConnectionString = BuildConnectionString(baseConnectionString, databaseName);
            await ExecuteNonQueryAsync(
                masterConnectionString,
                $"CREATE DATABASE {QuoteSqlServerName(databaseName)};");

            try
            {
                await ExecuteNonQueryAsync(
                    databaseConnectionString,
                    "ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;");
                await ExecuteNonQueryAsync(databaseConnectionString, SchemaAndSeedSql);
                var migrationSql = await File.ReadAllTextAsync(FindMigrationPath());
                migrationSql = migrationSql.Replace(
                    "IF DB_NAME() <> N'HBweb'",
                    $"IF DB_NAME() <> N'{databaseName}'",
                    StringComparison.Ordinal);
                await ExecuteNonQueryAsync(databaseConnectionString, migrationSql);
                return new RevenueSnapshotSqlServerFixture(
                    masterConnectionString,
                    databaseName,
                    databaseConnectionString);
            }
            catch
            {
                await DropDatabaseAsync(masterConnectionString, databaseName);
                throw;
            }
        }

        public SalesDashboardReactService CreateService(IMemoryCache? cache = null)
        {
            return new SalesDashboardReactService(
                CreateSqlSugarContext(_db),
                CreatePosmSqlSugarContext(_posmDb),
                Mock.Of<IMapper>(),
                NullLogger<SalesDashboardReactService>.Instance,
                cache ?? _cache);
        }

        public async Task SeedPublicationScenarioAsync()
        {
            var now = DateTime.UtcNow;
            await _db.Insertable(new[]
            {
                new StoreSalesStatistic
                {
                    Date = PublishedDate, BranchCode = "1003", BranchName = "一〇〇三店",
                    TotalAmount = 10m, TotalQuantity = 1, OrderCount = 1,
                    CustomerCount = 1, AverageOrderValue = 10m,
                },
                new StoreSalesStatistic
                {
                    Date = LegacyDate, BranchCode = "1003", BranchName = "一〇〇三店",
                    TotalAmount = 20m, TotalQuantity = 2, OrderCount = 2,
                    CustomerCount = 2, AverageOrderValue = 10m,
                },
                new StoreSalesStatistic
                {
                    Date = PublishedZeroDate, BranchCode = "1003", BranchName = "一〇〇三店",
                    TotalAmount = 0m, TotalQuantity = 0, OrderCount = 0,
                    CustomerCount = 0, AverageOrderValue = 0m,
                },
            }).ExecuteCommandAsync();
            await _db.Insertable(new[]
            {
                LegacyRow(PublishedDate, 9, 90m, 9),
                LegacyRow(LegacyDate, 9, 20m, 2),
                LegacyRow(PublishedZeroDate, 9, 70m, 7),
            }).ExecuteCommandAsync();
            await _db.Insertable(new[]
            {
                RefreshState("StoreSales", PublishedDate, now),
                RefreshState("StoreSales", LegacyDate, now),
                RefreshState("StoreSales", PublishedZeroDate, now),
                RefreshState("HourlySales", PublishedDate, now),
                RefreshState("HourlySales", LegacyDate, now),
                RefreshState("HourlySales", PublishedZeroDate, now),
            }).ExecuteCommandAsync();

            var batchId = Guid.NewGuid();
            await _db.Insertable(new HourlySalesBackfillBatch
            {
                Id = batchId, StartDate = PublishedDate, EndDate = PublishedZeroDate,
                RuleVersion = HourlySalesBackfillService.CurrentRuleVersion,
                Status = "Applied", RequestedBy = "integration-test",
                AppliedBy = "integration-test", CreatedAtUtc = now, UpdatedAtUtc = now,
            }).ExecuteCommandAsync();
            await _db.Insertable(new[]
            {
                AppliedDay(batchId, PublishedDate, "published-hash", 10m, 1, 1, now),
                AppliedDay(batchId, PublishedZeroDate, "zero-hash", 0m, 0, 0, now),
            }).ExecuteCommandAsync();
            await _db.Insertable(new HourlySalesBackfillPublishedRow
            {
                BatchId = batchId, Date = PublishedDate, Hour = 9,
                BranchCode = "1003", BranchName = "一〇〇三店",
                TotalAmount = 10m, TotalQuantity = 1, OrderCount = 1,
                CustomerCount = 1, AverageOrderValue = 10m, PublishedAtUtc = now,
            }).ExecuteCommandAsync();
        }

        public async Task LegacyFastestBulkCopyAsync(DateTime date)
        {
            await _db.Fastest<HourlySalesStatistic>().BulkCopyAsync(new List<HourlySalesStatistic>
            {
                LegacyRow(date, 10, 999m, 99),
            });
        }

        public Task<List<HourlySalesStatistic>> ReadViewAsync(DateTime date) =>
            _db.Queryable<HourlySalesStatistic>().AS("HourlySalesReadStatistic")
                .Where(row => row.Date == date.Date).OrderBy(row => row.Hour).ToListAsync();

        public Task<List<HourlySalesStatistic>> ReadPhysicalAsync(DateTime date) =>
            _db.Queryable<HourlySalesStatistic>()
                .Where(row => row.Date == date.Date).OrderBy(row => row.Hour).ToListAsync();

        public Task<int> CountPhysicalRowsAsync(DateTime date) =>
            _db.Queryable<HourlySalesStatistic>().Where(row => row.Date == date.Date).CountAsync();

        public Task<int> CountReadRowsAsync(DateTime start, DateTime end) =>
            _db.Queryable<HourlySalesStatistic>().AS("HourlySalesReadStatistic")
                .Where(row => row.Date >= start.Date && row.Date <= end.Date).CountAsync();

        public async Task MarkPublicationSourceDriftAsync(DateTime date)
        {
            await _db.Updateable<HourlySalesBackfillDay>()
                .SetColumns(row => row.Error == "source-drift:测试来源变化")
                .SetColumns(row => row.UpdatedAtUtc == DateTime.UtcNow.AddSeconds(1))
                .Where(row => row.Date == date.Date && row.Status == "Applied")
                .ExecuteCommandAsync();
        }

        public async Task MarkPublicationRuleUnsupportedAsync(DateTime date)
        {
            var batchId = await _db.Queryable<HourlySalesBackfillDay>()
                .Where(row => row.Date == date.Date && row.Status == "Applied")
                .Select(row => row.BatchId)
                .SingleAsync();
            await _db.Updateable<HourlySalesBackfillBatch>()
                .SetColumns(row => row.RuleVersion == "unsupported-v0")
                .SetColumns(row => row.UpdatedAtUtc == DateTime.UtcNow.AddSeconds(1))
                .Where(row => row.Id == batchId)
                .ExecuteCommandAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _cache.Dispose();
            _db.Dispose();
            _posmDb.Dispose();
            await DropDatabaseAsync(_masterConnectionString, _databaseName);
        }

        private static HourlySalesStatistic LegacyRow(DateTime date, int hour, decimal amount, int orders) => new()
        {
            Date = date, Hour = hour, BranchCode = "1003", BranchName = "一〇〇三店",
            TotalAmount = amount, TotalQuantity = orders, OrderCount = orders,
            CustomerCount = orders, AverageOrderValue = orders == 0 ? 0m : amount / orders,
            UpdateTime = DateTime.UtcNow,
        };

        private static SalesStatisticRefreshState RefreshState(string type, DateTime date, DateTime now) => new()
        {
            StatisticType = type, Date = date, Status = SalesStatisticRefreshStatus.Fresh,
            LastAggregatedAtUtc = now, CompletedAtUtc = now,
        };

        private static HourlySalesBackfillDay AppliedDay(
            Guid batchId, DateTime date, string hash, decimal amount, int orders, int rows, DateTime now) => new()
        {
            BatchId = batchId, Date = date, Status = "Applied",
            SourceHash = hash, BeforeHash = "legacy", AfterHash = hash,
            BeforeJson = "[]", CandidateJson = "[]", SourceStatusJson = "[]",
            ExpectedAmount = amount, CandidateAmount = amount,
            ExpectedOrderCount = orders, CandidateOrderCount = orders,
            RowCount = rows, UpdatedAtUtc = now,
        };

        private static readonly string SchemaAndSeedSql = """
            SET NOCOUNT ON;
            CREATE TABLE [dbo].[Store] (
                [StoreCode] nvarchar(50) NOT NULL PRIMARY KEY,
                [StoreName] nvarchar(100) NULL,
                [IsActive] bit NOT NULL,
                [IsDeleted] bit NOT NULL
            );
            CREATE TABLE [dbo].[StoreSalesStatistic] (
                [Date] datetime2(7) NOT NULL,
                [BranchCode] nvarchar(50) NOT NULL,
                [BranchName] nvarchar(100) NOT NULL,
                [TotalAmount] decimal(18,2) NOT NULL,
                [TotalQuantity] int NOT NULL CONSTRAINT [DF_StoreSalesStatistic_TotalQuantity] DEFAULT 0,
                [OrderCount] int NOT NULL,
                [CustomerCount] int NOT NULL CONSTRAINT [DF_StoreSalesStatistic_CustomerCount] DEFAULT 0,
                [AverageOrderValue] decimal(18,2) NOT NULL CONSTRAINT [DF_StoreSalesStatistic_AverageOrderValue] DEFAULT 0,
                [UpdateTime] datetime2(7) NOT NULL CONSTRAINT [DF_StoreSalesStatistic_UpdateTime] DEFAULT SYSUTCDATETIME(),
                CONSTRAINT [PK_StoreSalesStatistic] PRIMARY KEY ([Date], [BranchCode])
            );
            CREATE TABLE [dbo].[HourlySalesStatistic] (
                [Date] datetime2(7) NOT NULL,
                [Hour] int NOT NULL,
                [BranchCode] nvarchar(50) NOT NULL,
                [BranchName] nvarchar(100) NULL,
                [TotalAmount] decimal(18,2) NOT NULL,
                [OrderCount] int NULL,
                [TotalQuantity] int NOT NULL CONSTRAINT [DF_HourlySalesStatistic_TotalQuantity] DEFAULT 0,
                [CustomerCount] int NOT NULL CONSTRAINT [DF_HourlySalesStatistic_CustomerCount] DEFAULT 0,
                [AverageOrderValue] decimal(18,2) NOT NULL CONSTRAINT [DF_HourlySalesStatistic_AverageOrderValue] DEFAULT 0,
                [UpdateTime] datetime2(7) NOT NULL CONSTRAINT [DF_HourlySalesStatistic_UpdateTime] DEFAULT SYSUTCDATETIME(),
                CONSTRAINT [PK_HourlySalesStatistic] PRIMARY KEY ([Date], [Hour], [BranchCode])
            );
            CREATE TABLE [dbo].[SalesStatisticRefreshState] (
                [StatisticType] nvarchar(80) NOT NULL,
                [Date] datetime2(7) NOT NULL,
                [Status] nvarchar(20) NOT NULL,
                [LastSourceUploadTime] datetime2(7) NULL,
                [SourceTimeZone] nvarchar(40) NOT NULL CONSTRAINT [DF_SalesStatisticRefreshState_SourceTimeZone] DEFAULT N'POSM_LOCAL',
                [SourceProductVersion] nvarchar(64) NULL,
                [LastAggregatedAtUtc] datetime2(7) NULL,
                [LastCheckedAtUtc] datetime2(7) NULL,
                [ErrorMessage] nvarchar(1000) NULL,
                [JobId] uniqueidentifier NULL,
                [RequestedBy] nvarchar(100) NULL,
                [RequestedAtUtc] datetime2(7) NULL,
                [StartedAtUtc] datetime2(7) NULL,
                [CompletedAtUtc] datetime2(7) NULL,
                CONSTRAINT [PK_SalesStatisticRefreshState] PRIMARY KEY ([StatisticType], [Date])
            );

            INSERT INTO [dbo].[Store] ([StoreCode], [StoreName], [IsActive], [IsDeleted]) VALUES
                (N'1003', N'一〇〇三店', 1, 0),
                (N'1004', N'一〇〇四店', 1, 0),
                (N'1005', N'一〇〇五店', 1, 0),
                (N'1009', N'一〇〇九店', 1, 0),
                (N'1012', N'一〇一二店', 1, 0),
                (N'1014', N'一〇一四店', 1, 0),
                (N'1015', N'一〇一五店', 1, 0),
                (N'1024', N'一〇二四店', 1, 0),
                (N'OUTSIDE', N'店外测试', 1, 0);

            INSERT INTO [dbo].[StoreSalesStatistic] ([Date], [BranchCode], [BranchName], [TotalAmount], [OrderCount]) VALUES
                ('2026-09-07', N'1003', N'一〇〇三店', 100.13, 3),
                ('2026-09-07', N'1004', N'一〇〇四店', 200.24, 4),
                ('2026-09-07', N'1005', N'一〇〇五店', 300.35, 5),
                ('2026-09-07', N'1009', N'一〇〇九店', 400.49, 6),
                ('2026-09-07', N'1012', N'一〇一二店', 500.12, 7),
                ('2026-09-07', N'1014', N'一〇一四店', 600.14, 8),
                ('2026-09-07', N'1015', N'一〇一五店', 700.15, 9),
                ('2026-09-07', N'1024', N'一〇二四店', 800.24, 10),
                ('2026-09-07', N'OUTSIDE', N'店外测试', 999.99, 11);

            INSERT INTO [dbo].[HourlySalesStatistic] ([Date], [Hour], [BranchCode], [BranchName], [TotalAmount], [OrderCount]) VALUES
                ('2026-09-07', 9, N'1003', N'一〇〇三店', 100.13, 3),
                ('2026-09-07', 9, N'1004', N'一〇〇四店', 200.24, 4),
                ('2026-09-07', 9, N'1005', N'一〇〇五店', 300.35, 5),
                ('2026-09-07', 9, N'1009', N'一〇〇九店', 400.49, 6),
                ('2026-09-07', 9, N'1012', N'一〇一二店', 500.12, 7),
                ('2026-09-07', 9, N'1014', N'一〇一四店', 600.14, 8),
                ('2026-09-07', 9, N'1015', N'一〇一五店', 700.15, 9),
                ('2026-09-07', 9, N'1024', N'一〇二四店', 800.24, 10),
                ('2026-09-07', 9, N'OUTSIDE', N'店外测试', 999.99, 11);

            INSERT INTO [dbo].[SalesStatisticRefreshState]
                ([StatisticType], [Date], [Status], [LastAggregatedAtUtc], [CompletedAtUtc]) VALUES
                (N'StoreSales', '2026-09-07', N'Fresh', '2026-09-07T03:32:59', '2026-09-07T03:32:59'),
                (N'HourlySales', '2026-09-07', N'Fresh', '2026-09-07T03:32:59', '2026-09-07T03:32:59');
            """;

        private static ConnectionConfig CreateConnectionConfig(string connectionString) => new()
        {
            ConnectionString = connectionString,
            DbType = DbType.SqlServer,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        };

        private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
        {
            var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
            typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(context, db);
            return context;
        }

        private static POSMSqlSugarContext CreatePosmSqlSugarContext(ISqlSugarClient db)
        {
            var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(POSMSqlSugarContext));
            typeof(POSMSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(context, db);
            return context;
        }

        private static string BuildConnectionString(string connectionString, string databaseName)
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = databaseName,
                PersistSecurityInfo = true,
            };
            return builder.ConnectionString;
        }

        private static string FindMigrationPath()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "services", "backend", "BlazorApp.Api",
                    "Data", "Migrations", "20260915_CreateHourlySalesBackfill.sql");
                if (File.Exists(candidate))
                    return candidate;
                directory = directory.Parent;
            }
            throw new FileNotFoundException("找不到分时回填 SQL Server 迁移文件。");
        }

        private static void EnsureLoopbackSqlServer(string connectionString)
        {
            var dataSource = new SqlConnectionStringBuilder(connectionString).DataSource.Trim();
            if (dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
                dataSource = dataSource[4..];
            var parts = dataSource.Split(',', 2, StringSplitOptions.TrimEntries);
            var host = parts[0].Trim().Trim('[', ']');
            if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                && !host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{SqlServerTestConnectionEnvVar} 必须指向 localhost 或 127.0.0.1，当前 DataSource 不符合环回测试约束。");
            }
            if (parts.Length == 2
                && (!int.TryParse(parts[1], out var port) || port is < 1 or > 65535))
            {
                throw new InvalidOperationException(
                    $"{SqlServerTestConnectionEnvVar} 的 SQL Server 端口无效，当前测试仅允许本地环回连接。");
            }
        }

        private static async Task ExecuteNonQueryAsync(string connectionString, string sql)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            await command.ExecuteNonQueryAsync();
        }

        private static async Task DropDatabaseAsync(string masterConnectionString, string databaseName)
        {
            var quotedName = QuoteSqlServerName(databaseName);
            await ExecuteNonQueryAsync(
                masterConnectionString,
                $"""
                IF DB_ID(N'{databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE {quotedName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE {quotedName};
                END;
                """);
        }

        private static string QuoteSqlServerName(string name)
        {
            return $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";
        }
    }
}
