using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBSalesRecord;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class HourlySalesBackfillSqlServerFactAttribute : FactAttribute
{
    internal const string ConnectionEnvironmentVariable = "HBWEB_SCHEMA_SQLSERVER_TEST_CONNECTION";

    public HourlySalesBackfillSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
            Skip = $"未配置 {ConnectionEnvironmentVariable}，跳过分时回填 SQL Server 事务测试。";
    }
}

[Trait("Category", "SQL")]
public sealed class HourlySalesBackfillSqlServerTests
{
    [HourlySalesBackfillSqlServerFact]
    public async Task 只读预览按权威HBSales口径排除类型二和零值行且不写审计表()
    {
        await using var fixture = await Fixture.CreateAsync();
        var date = new DateTime(2025, 9, 15);
        await fixture.SeedHistoryAsync(date);

        var preview = await fixture.Service.PreviewDayReadOnlyAsync(date, CancellationToken.None);

        Assert.True(preview.Valid, string.Join("; ", preview.Issues));
        Assert.Equal(10m, preview.ExpectedAmount);
        Assert.Equal(10m, preview.CandidateAmount);
        Assert.Equal(2, preview.ExpectedQuantity);
        Assert.Equal(2, preview.CandidateQuantity);
        Assert.Equal(1, preview.ExpectedOrderCount);
        Assert.Equal(1, preview.CandidateOrderCount);
        Assert.Equal(2, preview.CandidateRows.Count);
        Assert.All(preview.CandidateRows, row => Assert.Equal(11, row.Hour));
        Assert.Contains(preview.CandidateRows,
            row => row.BranchCode == "S1" && row.TotalAmount == 10m
                && row.TotalQuantity == 2 && row.OrderCount == 1);
        Assert.Contains(preview.SourceStatuses,
            status => status.Source == "POSM" && status.State == "Empty");
        Assert.Contains(preview.SourceStatuses,
            status => status.Source == "HBSales" && status.State == "Success");
        Assert.Equal(0, await fixture.Db.Queryable<HourlySalesBackfillBatch>().CountAsync());
        Assert.Equal(0, await fixture.Db.Queryable<HourlySalesBackfillDay>().CountAsync());
    }

    [HourlySalesBackfillSqlServerFact]
    public async Task HBSales缺码沿用日统计唯一映射_歧义时失败关闭()
    {
        await using var fixture = await Fixture.CreateAsync();
        var validDate = new DateTime(2025, 9, 16);
        var blockedDate = validDate.AddDays(1);
        await fixture.Db.Insertable(new[]
        {
            new Product { UUID = "PRODUCT-1", ProductCode = "P1", ItemNumber = "ITEM-UNIQUE" },
            new Product { UUID = "PRODUCT-2", ProductCode = "P2", ItemNumber = "ITEM-AMBIGUOUS" },
            new Product { UUID = "PRODUCT-3", ProductCode = "P3", ItemNumber = "ITEM-AMBIGUOUS" },
        }).ExecuteCommandAsync();
        await fixture.Db.Insertable(new[]
        {
            new StoreSalesStatistic
            {
                Date = validDate, BranchCode = "S1", BranchName = "Glendale",
                TotalAmount = 5m, TotalQuantity = 1, OrderCount = 1,
            },
            new StoreSalesStatistic
            {
                Date = blockedDate, BranchCode = "S1", BranchName = "Glendale",
                TotalAmount = 6m, TotalQuantity = 1, OrderCount = 1,
            },
        }).ExecuteCommandAsync();
        await fixture.History.Insertable(new[]
        {
            new SalesOrderMain
            {
                ID = 101, B销售单号 = "MISSING-UNIQUE", B结账日期 = validDate,
                B结账时间 = TimeSpan.FromHours(12), B分店代码 = "S1", B单据类型 = "1",
            },
            new SalesOrderMain
            {
                ID = 102, B销售单号 = "MISSING-AMBIGUOUS", B结账日期 = blockedDate,
                B结账时间 = TimeSpan.FromHours(13), B分店代码 = "S1", B单据类型 = "1",
            },
        }).ExecuteCommandAsync();
        await fixture.History.Insertable(new[]
        {
            new SalesOrderDetailRecord
            {
                ID = 101, B销售单号 = "MISSING-UNIQUE", B结账日期 = validDate,
                B结账时间 = TimeSpan.FromHours(9), B分店代码 = "S1", B产品编号 = null,
                B货号 = "ITEM-UNIQUE", B合计金额 = 5m, B数量 = 1m,
            },
            new SalesOrderDetailRecord
            {
                ID = 102, B销售单号 = "MISSING-AMBIGUOUS", B结账日期 = blockedDate,
                B结账时间 = TimeSpan.FromHours(10), B分店代码 = "S1", B产品编号 = null,
                B货号 = "ITEM-AMBIGUOUS", B合计金额 = 6m, B数量 = 1m,
            },
        }).ExecuteCommandAsync();

        var valid = await fixture.Service.PreviewDayReadOnlyAsync(validDate, CancellationToken.None);
        Assert.True(valid.Valid, string.Join("; ", valid.Issues));
        Assert.Contains(valid.CandidateRows,
            row => row.BranchCode == "S1" && row.Hour == 12 && row.TotalAmount == 5m);

        var blocked = await fixture.Service.PreviewDayReadOnlyAsync(blockedDate, CancellationToken.None);
        Assert.False(blocked.Valid);
        Assert.Contains(blocked.SourceStatuses,
            status => status.Source == "HBSales" && status.State == "Unavailable");
        Assert.Contains(blocked.Issues, issue => issue.StartsWith("source-unavailable:"));
    }

    [HourlySalesBackfillSqlServerFact]
    public async Task POSM退款按PaymentDetail与订单明细口径_不会被退货记录二次扣减()
    {
        await using var fixture = await Fixture.CreateAsync();
        var date = new DateTime(2026, 2, 3);
        await fixture.Db.Insertable(new StoreSalesStatistic
        {
            Date = date, BranchCode = "S1", BranchName = "Glendale",
            TotalAmount = -5m, TotalQuantity = -1, OrderCount = 1,
        }).ExecuteCommandAsync();
        await fixture.Posm.Insertable(new SalesOrder
        {
            OrderGuid = "RETURN-ORDER", BranchCode = "S1", Status = 1,
            OrderTime = date.AddHours(14), LastUploadTime = date.AddHours(15),
        }).ExecuteCommandAsync();
        await fixture.Posm.Insertable(new PaymentDetail
        {
            PaymentGuid = "RETURN-PAYMENT", OrderGuid = "RETURN-ORDER", Amount = -5m,
        }).ExecuteCommandAsync();
        await fixture.Posm.Insertable(new SalesOrderDetail
        {
            OrderDetailGuid = "RETURN-ORDER-DETAIL", OrderGuid = "RETURN-ORDER",
            ProductCode = "P1", Quantity = -1, ActualAmount = -5m,
        }).ExecuteCommandAsync();
        await fixture.Posm.Insertable(new SalesReturnRecord
        {
            ReturnDetailGuid = "RETURN-DETAIL", ReturnOrderGuid = "RETURN-ORDER",
            ProductCode = "P1", ReturnQuantity = 1m, ReturnAmount = 5m,
            CreatedTime = date.AddHours(15), UpdatedTime = date.AddHours(15),
        }).ExecuteCommandAsync();

        var first = await fixture.Service.PreviewDayReadOnlyAsync(date, CancellationToken.None);
        Assert.True(first.Valid, string.Join("; ", first.Issues));
        Assert.Equal(-5m, first.CandidateAmount);
        Assert.Equal(-1, first.CandidateQuantity);
        Assert.Contains(first.CandidateRows,
            row => row.BranchCode == "S1" && row.Hour == 14 && row.TotalAmount == -5m);

        await fixture.Posm.Updateable<SalesReturnRecord>()
            .SetColumns(row => row.ReturnAmount == 6m)
            .SetColumns(row => row.UpdatedTime == date.AddHours(16))
            .Where(row => row.ReturnDetailGuid == "RETURN-DETAIL").ExecuteCommandAsync();
        var second = await fixture.Service.PreviewDayReadOnlyAsync(date, CancellationToken.None);
        Assert.True(second.Valid, string.Join("; ", second.Issues));
        Assert.Equal(-5m, second.CandidateAmount);
        Assert.Equal(-1, second.CandidateQuantity);
        Assert.Equal(first.SourceHash, second.SourceHash);
    }

    [HourlySalesBackfillSqlServerFact]
    public async Task 近期空来源必须有FreshPublished日终证据才可认证真零()
    {
        await using var fixture = await Fixture.CreateAsync();
        var recent = SalesStatisticsBusinessDate.Today().AddDays(-1);
        var blockedBatch = await fixture.Service.PreviewAsync(recent, recent, "无水位预览人");
        await fixture.DrainAsync();
        var blocked = await fixture.Db.Queryable<HourlySalesBackfillDay>()
            .Where(row => row.BatchId == blockedBatch).SingleAsync();
        Assert.Equal("Blocked", blocked.Status);
        Assert.Contains("source-unavailable", blocked.Error);

        var now = DateTime.UtcNow;
        await fixture.Db.Insertable(new[]
        {
            new SalesStatisticRefreshState
            {
                StatisticType = SalesStatisticType.ProductStoreDaily, Date = recent,
                Status = SalesStatisticRefreshStatus.Fresh, LastCheckedAtUtc = now,
                CompletedAtUtc = now,
            },
            new SalesStatisticRefreshState
            {
                StatisticType = SalesStatisticType.RevenueReportPublished, Date = recent,
                Status = SalesStatisticRefreshStatus.Fresh, LastCheckedAtUtc = now,
                CompletedAtUtc = now,
            },
        }).ExecuteCommandAsync();
        var validBatch = await fixture.Service.PreviewAsync(recent, recent, "有水位预览人");
        await fixture.DrainAsync();
        var valid = await fixture.Db.Queryable<HourlySalesBackfillDay>()
            .Where(row => row.BatchId == validBatch).SingleAsync();
        Assert.Equal("Previewed", valid.Status);
        Assert.Equal(0, valid.RowCount);
    }

    [HourlySalesBackfillSqlServerFact]
    public async Task 发布版本原子切换_旧原表写入不影响视图_漂移时可安全回滚重发()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True(fixture.Service.SchemaReady());
        await fixture.Db.Ado.ExecuteCommandAsync(
            "ALTER INDEX UX_HourlySalesBackfillDay_OneAppliedPerDate ON dbo.HourlySalesBackfillDay DISABLE");
        Assert.False(fixture.Service.SchemaReady());
        await fixture.Db.Ado.ExecuteCommandAsync(
            "ALTER INDEX UX_HourlySalesBackfillDay_OneAppliedPerDate ON dbo.HourlySalesBackfillDay REBUILD");
        Assert.True(fixture.Service.SchemaReady());
        await fixture.Db.Ado.ExecuteCommandAsync(
            "DISABLE TRIGGER dbo.TR_HourlySalesBackfillPublishedRow_Immutable ON dbo.HourlySalesBackfillPublishedRow");
        Assert.False(fixture.Service.SchemaReady());
        await fixture.Db.Ado.ExecuteCommandAsync(
            "ENABLE TRIGGER dbo.TR_HourlySalesBackfillPublishedRow_Immutable ON dbo.HourlySalesBackfillPublishedRow");
        Assert.True(fixture.Service.SchemaReady());
        await fixture.SeedAsync();

        var legacyBefore = await fixture.Db.Queryable<HourlySalesStatistic>()
            .Where(row => row.Date == Fixture.Date).SingleAsync();
        Assert.Equal(7m, legacyBefore.TotalAmount);

        var batchId = await fixture.Service.PreviewAsync(Fixture.Date, Fixture.Date, "预览人");
        await fixture.DrainAsync();
        Assert.True(await fixture.Service.RequestAsync(batchId, false, "执行人"));
        await fixture.DrainAsync();

        var legacyAfterApply = await fixture.Db.Queryable<HourlySalesStatistic>()
            .Where(row => row.Date == Fixture.Date).SingleAsync();
        Assert.Equal(7m, legacyAfterApply.TotalAmount);
        var publishedRows = await fixture.Db.Queryable<HourlySalesBackfillPublishedRow>()
            .Where(row => row.BatchId == batchId && row.Date == Fixture.Date).ToListAsync();
        var appliedDay = await fixture.Db.Queryable<HourlySalesBackfillDay>()
            .Where(row => row.BatchId == batchId).SingleAsync();
        Assert.Equal("Applied", appliedDay.Status);
        Assert.Equal(HourlySalesBackfillService.PublishedTargetHash(publishedRows), appliedDay.AfterHash);
        Assert.Contains(publishedRows, row => row.BranchCode == "S1"
            && row.BranchName == "Glendale" && row.AverageOrderValue == 33.33m);
        var publishedView = await fixture.ReadViewAsync(Fixture.Date);
        Assert.Equal(2, publishedView.Count);
        Assert.DoesNotContain(publishedView, row => row.TotalAmount == 7m);
        Assert.True(await fixture.Service.RevalidateAsync(batchId, Fixture.Date, CancellationToken.None));

        await fixture.Posm.Updateable<PaymentDetail>()
            .SetColumns(row => row.Amount == 35m)
            .Where(row => row.PaymentGuid == "PAY-3").ExecuteCommandAsync();
        Assert.False(await fixture.Service.RevalidateAsync(batchId, Fixture.Date, CancellationToken.None));
        var driftedDay = await fixture.Db.Queryable<HourlySalesBackfillDay>()
            .Where(row => row.BatchId == batchId).SingleAsync();
        Assert.StartsWith("source-drift:", driftedDay.Error);
        Assert.Empty(await fixture.ReadViewAsync(Fixture.Date));
        await fixture.Posm.Updateable<PaymentDetail>()
            .SetColumns(row => row.Amount == 34m)
            .Where(row => row.PaymentGuid == "PAY-3").ExecuteCommandAsync();
        Assert.True(await fixture.Service.RevalidateAsync(batchId, Fixture.Date, CancellationToken.None));

        var duplicateBatch = await fixture.Service.PreviewAsync(Fixture.Date, Fixture.Date, "第二预览人");
        await fixture.DrainAsync();
        Assert.True(await fixture.Service.RequestAsync(duplicateBatch, false, "第二执行人"));
        await fixture.DrainAsync();
        Assert.Equal("Blocked", (await fixture.Db.Queryable<HourlySalesBackfillBatch>()
            .InSingleAsync(duplicateBatch)).Status);
        Assert.Equal("Failed", (await fixture.Db.Queryable<HourlySalesBackfillDay>()
            .Where(row => row.BatchId == duplicateBatch).SingleAsync()).Status);

        // 模拟仍在运行的旧实例：它可继续更新/批量写原表，但整日发布视图仍只返回已发布版本。
        await fixture.Db.Updateable<HourlySalesStatistic>()
            .SetColumns(row => row.TotalAmount == 1m)
            .Where(row => row.Date == Fixture.Date).ExecuteCommandAsync();
        await fixture.Db.Fastest<HourlySalesStatistic>().BulkCopyAsync(new List<HourlySalesStatistic>
        {
            new()
            {
                Date = Fixture.Date, Hour = 10, BranchCode = "OLD", BranchName = "旧写入器",
                TotalAmount = 9m, TotalQuantity = 1, OrderCount = 1,
                CustomerCount = 1, AverageOrderValue = 9m,
            },
        });
        Assert.Equal(2, await fixture.Db.Queryable<HourlySalesStatistic>()
            .Where(row => row.Date == Fixture.Date).CountAsync());
        var stillPublishedView = await fixture.ReadViewAsync(Fixture.Date);
        Assert.Equal(2, stillPublishedView.Count);
        Assert.DoesNotContain(stillPublishedView, row => row.BranchCode == "OLD" || row.TotalAmount == 1m);
        Assert.Equal(appliedDay.AfterHash, HourlySalesBackfillService.PublishedTargetHash(
            await fixture.Db.Queryable<HourlySalesBackfillPublishedRow>()
                .Where(row => row.BatchId == batchId && row.Date == Fixture.Date).ToListAsync()));

        // 发布版本只追加：数据库拒绝旁路更新/删除，读视图始终保持认证快照。
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Db.Updateable<HourlySalesBackfillPublishedRow>()
            .SetColumns(row => row.TotalAmount == 101m)
            .Where(row => row.BatchId == batchId && row.Date == Fixture.Date && row.BranchCode == "S1")
            .ExecuteCommandAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Db.Deleteable<HourlySalesBackfillPublishedRow>()
            .Where(row => row.BatchId == batchId && row.Date == Fixture.Date && row.BranchCode == "S1")
            .ExecuteCommandAsync());
        Assert.Equal(appliedDay.AfterHash, HourlySalesBackfillService.PublishedTargetHash(
            await fixture.Db.Queryable<HourlySalesBackfillPublishedRow>()
                .Where(row => row.BatchId == batchId && row.Date == Fixture.Date).ToListAsync()));

        Assert.True(await fixture.Service.RequestAsync(batchId, true, "回滚人"));
        await fixture.DrainAsync();
        var finalDay = await fixture.Db.Queryable<HourlySalesBackfillDay>()
            .Where(row => row.BatchId == batchId).SingleAsync();
        Assert.Equal("RolledBack", finalDay.Status);
        Assert.Null(finalDay.Error);
        Assert.Equal(2, await fixture.Db.Queryable<HourlySalesBackfillPublishedRow>()
            .Where(row => row.BatchId == batchId).CountAsync());
        var legacyView = await fixture.ReadViewAsync(Fixture.Date);
        Assert.Equal(2, legacyView.Count);
        Assert.Contains(legacyView, row => row.BranchCode == "OLD" && row.TotalAmount == 9m);

        var replacementBatch = await fixture.Service.PreviewAsync(Fixture.Date, Fixture.Date, "重新预览人");
        await fixture.DrainAsync();
        Assert.True(await fixture.Service.RequestAsync(replacementBatch, false, "重新执行人"));
        await fixture.DrainAsync();
        var replacementDay = await fixture.Db.Queryable<HourlySalesBackfillDay>()
            .Where(row => row.BatchId == replacementBatch).SingleAsync();
        Assert.Equal("Applied", replacementDay.Status);
        Assert.Null(replacementDay.Error);
        Assert.Equal(replacementDay.AfterHash, HourlySalesBackfillService.PublishedTargetHash(
            await fixture.Db.Queryable<HourlySalesBackfillPublishedRow>()
                .Where(row => row.BatchId == replacementBatch).ToListAsync()));
        Assert.DoesNotContain(await fixture.ReadViewAsync(Fixture.Date), row => row.BranchCode == "OLD");
    }

    [HourlySalesBackfillSqlServerFact]
    public async Task 已认证真零整日覆盖旧原表_回滚后恢复旧表读取()
    {
        await using var fixture = await Fixture.CreateAsync();
        var zeroDate = new DateTime(2026, 2, 2);
        await fixture.Db.Insertable(new HourlySalesStatistic
        {
            Date = zeroDate, Hour = 9, BranchCode = "STALE", BranchName = "旧错误数据",
            TotalAmount = 88m, TotalQuantity = 8, OrderCount = 1,
            CustomerCount = 1, AverageOrderValue = 88m,
        }).ExecuteCommandAsync();

        var batchId = await fixture.Service.PreviewAsync(zeroDate, zeroDate, "真零预览人");
        await fixture.DrainAsync();
        var previewed = await fixture.Db.Queryable<HourlySalesBackfillDay>()
            .Where(row => row.BatchId == batchId).SingleAsync();
        Assert.Equal("Previewed", previewed.Status);
        Assert.Equal(0, previewed.RowCount);
        Assert.True(await fixture.Service.RequestAsync(batchId, false, "真零发布人"));
        await fixture.DrainAsync();

        var applied = await fixture.Db.Queryable<HourlySalesBackfillDay>()
            .Where(row => row.BatchId == batchId).SingleAsync();
        Assert.Equal("Applied", applied.Status);
        Assert.Equal(HourlySalesBackfillService.PublishedTargetHash([]), applied.AfterHash);
        Assert.Empty(await fixture.ReadViewAsync(zeroDate));
        Assert.Equal(88m, (await fixture.Db.Queryable<HourlySalesStatistic>()
            .Where(row => row.Date == zeroDate).SingleAsync()).TotalAmount);

        Assert.True(await fixture.Service.RequestAsync(batchId, true, "真零回滚人"));
        await fixture.DrainAsync();
        Assert.Equal("RolledBack", (await fixture.Db.Queryable<HourlySalesBackfillDay>()
            .Where(row => row.BatchId == batchId).SingleAsync()).Status);
        Assert.Equal(88m, Assert.Single(await fixture.ReadViewAsync(zeroDate)).TotalAmount);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal static readonly DateTime Date = new(2026, 2, 1);
        internal readonly SqlSugarClient Db;
        internal readonly SqlSugarContext DbContext;
        internal readonly HourlySalesBackfillService Service;
        internal readonly string ConnectionString;
        private readonly SqlSugarClient _posm;
        internal SqlSugarClient Posm => _posm;
        private readonly SqlSugarScope _history;
        internal SqlSugarScope History => _history;
        private readonly SqlSugarClient _master;
        private readonly string _database;

        private Fixture(string masterConnection, string database, string connection)
        {
            _database = database;
            ConnectionString = connection;
            _master = Client(masterConnection);
            Db = Client(connection);
            _posm = Client(connection);
            _history = new SqlSugarScope(Config(connection));
            DbContext = new SqlSugarContext(Db, NullLogger<SqlSugarContext>.Instance);
            Service = new HourlySalesBackfillService(
                DbContext,
                Inject<POSMSqlSugarContext>(_posm),
                new HBSalesRecordSqlSugarContext(_history),
                NullLogger<HourlySalesBackfillService>.Instance);
        }

        internal static async Task<Fixture> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable(
                HourlySalesBackfillSqlServerFactAttribute.ConnectionEnvironmentVariable)!;
            var builder = new SqlConnectionStringBuilder(configured);
            // SqlSugar 自动关闭再打开连接时仍需保留密码；仅用于本机临时测试库。
            builder.PersistSecurityInfo = true;
            var dataSource = builder.DataSource.ToLowerInvariant();
            if (!(dataSource.Contains("localhost") || dataSource.Contains("127.0.0.1")))
                throw new InvalidOperationException($"SQL Server 集成测试只允许本机实例，实际 DataSource={builder.DataSource}");
            builder.InitialCatalog = "master";
            var masterConnection = builder.ConnectionString;
            var database = $"HbHourlyBackfill_{Guid.NewGuid():N}";
            var master = Client(masterConnection);
            try { await master.Ado.ExecuteCommandAsync($"CREATE DATABASE [{database}]"); }
            finally { master.Dispose(); }
            builder.InitialCatalog = database;
            var fixture = new Fixture(masterConnection, database, builder.ConnectionString);
            try
            {
                fixture.Db.CodeFirst.InitTables(
                    typeof(StoreSalesStatistic), typeof(HourlySalesStatistic),
                    typeof(HourlySalesBackfillBatch), typeof(HourlySalesBackfillDay),
                    typeof(HourlySalesBackfillPublishedRow),
                    typeof(SalesStatisticRefreshState), typeof(Product));
                // 生产迁移将审计影像保存为 nvarchar(max)。测试夹具由 CodeFirst 建最小表，
                // 显式对齐长 JSON 列，避免大候选快照被 SqlSugar 默认字符串长度截断。
                await fixture.Db.Ado.ExecuteCommandAsync(
                    "ALTER TABLE dbo.HourlySalesBackfillDay ALTER COLUMN BeforeJson nvarchar(max) NULL; "
                    + "ALTER TABLE dbo.HourlySalesBackfillDay ALTER COLUMN CandidateJson nvarchar(max) NULL; "
                    + "ALTER TABLE dbo.HourlySalesBackfillDay ALTER COLUMN SourceStatusJson nvarchar(max) NULL; "
                    + "ALTER TABLE dbo.HourlySalesBackfillPublishedRow ALTER COLUMN TotalAmount decimal(18,2) NOT NULL; "
                    + "ALTER TABLE dbo.HourlySalesBackfillPublishedRow ALTER COLUMN AverageOrderValue decimal(18,2) NOT NULL;");
                await fixture.Db.Ado.ExecuteCommandAsync(
                    "CREATE UNIQUE INDEX UX_HourlySalesBackfillDay_OneAppliedPerDate "
                    + "ON dbo.HourlySalesBackfillDay(Date) WHERE Status = N'Applied'");
                await fixture.Db.Ado.ExecuteCommandAsync(
                    "CREATE OR ALTER TRIGGER dbo.TR_HourlySalesBackfillPublishedRow_Immutable "
                    + "ON dbo.HourlySalesBackfillPublishedRow AFTER UPDATE, DELETE AS "
                    + "BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted) "
                    + "THROW 51004, N'分时发布版本不可直接修改或删除，请回滚指针后创建新版本', 1; END;");
                fixture._posm.CodeFirst.InitTables(
                    typeof(SalesOrder), typeof(PaymentDetail),
                    typeof(SalesOrderDetail), typeof(POSM_设备注册信息表),
                    typeof(SalesReturnRecord));
                fixture._history.CodeFirst.InitTables(typeof(SalesOrderMain), typeof(SalesOrderDetailRecord));
                await fixture.Db.Ado.ExecuteCommandAsync(
                    """
                    CREATE OR ALTER VIEW dbo.HourlySalesReadStatistic AS
                    SELECT published.[Date], published.[Hour], published.BranchCode,
                        published.BranchName, published.TotalAmount, published.TotalQuantity,
                        CAST(published.OrderCount AS int) AS OrderCount,
                        published.CustomerCount, published.AverageOrderValue,
                        published.PublishedAtUtc AS UpdateTime
                    FROM dbo.HourlySalesBackfillPublishedRow published
                    INNER JOIN dbo.HourlySalesBackfillDay manifest
                        ON manifest.BatchId = published.BatchId
                        AND manifest.[Date] = published.[Date]
                        AND manifest.[Status] = N'Applied'
                        AND (manifest.Error IS NULL OR LTRIM(RTRIM(manifest.Error)) = N'')
                    INNER JOIN dbo.HourlySalesBackfillBatch batch
                        ON batch.Id = manifest.BatchId
                        AND batch.RuleVersion = N'hourly-posm-hbsales-v1'
                    UNION ALL
                    SELECT legacy.[Date], legacy.[Hour], legacy.BranchCode,
                        legacy.BranchName, legacy.TotalAmount, legacy.TotalQuantity,
                        legacy.OrderCount, legacy.CustomerCount, legacy.AverageOrderValue,
                        legacy.UpdateTime
                    FROM dbo.HourlySalesStatistic legacy
                    WHERE NOT EXISTS (
                        SELECT 1 FROM dbo.HourlySalesBackfillDay manifest
                        WHERE manifest.[Date] = legacy.[Date]
                          AND manifest.[Status] = N'Applied')
                    """);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal async Task SeedAsync()
        {
            await Db.Insertable(new StoreSalesStatistic
            {
                Date = Date, BranchCode = "S1", BranchName = "Glendale",
                TotalAmount = 100m, TotalQuantity = 3, OrderCount = 3,
                CustomerCount = 3, AverageOrderValue = 100m / 3m,
            }).ExecuteCommandAsync();
            await Db.Insertable(new HourlySalesStatistic
            {
                Date = Date, Hour = 9, BranchCode = "S1", BranchName = "Glendale",
                TotalAmount = 7m, TotalQuantity = 1, OrderCount = 1,
                CustomerCount = 1, AverageOrderValue = 7m,
            }).ExecuteCommandAsync();
            for (var index = 1; index <= 3; index++)
            {
                var id = $"ORDER-{index}";
                await _posm.Insertable(new SalesOrder
                {
                    OrderGuid = id, BranchCode = "S1", Status = 1,
                    OrderTime = Date.AddHours(9).AddMinutes(index),
                }).ExecuteCommandAsync();
                await _posm.Insertable(new PaymentDetail
                {
                    PaymentGuid = $"PAY-{index}", OrderGuid = id,
                    Amount = index == 3 ? 34m : 33m,
                }).ExecuteCommandAsync();
                await _posm.Insertable(new SalesOrderDetail
                {
                    OrderDetailGuid = $"DETAIL-{index}", OrderGuid = id,
                    ProductCode = $"P{index}", Quantity = 1,
                }).ExecuteCommandAsync();
            }
        }

        internal async Task SeedHistoryAsync(DateTime date)
        {
            await Db.Insertable(new StoreSalesStatistic
            {
                Date = date, BranchCode = "S1", BranchName = "Glendale",
                TotalAmount = 10m, TotalQuantity = 2, OrderCount = 1,
                CustomerCount = 1, AverageOrderValue = 10m,
            }).ExecuteCommandAsync();
            var now = DateTime.UtcNow;
            await _history.Insertable(new[]
            {
                new SalesOrderMain { ID = 1, B销售单号 = "VALID", B结账日期 = date,
                    B结账时间 = TimeSpan.FromHours(11), B分店代码 = "S1",
                    B单据类型 = "1", FGC_CreateDate = now },
                new SalesOrderMain { ID = 2, B销售单号 = "TYPE2", B结账日期 = date,
                    B结账时间 = TimeSpan.FromHours(10), B分店代码 = "S1",
                    B单据类型 = "2", FGC_CreateDate = now },
                new SalesOrderMain { ID = 3, B销售单号 = "NO-PRODUCT", B结账日期 = date,
                    B结账时间 = TimeSpan.FromHours(11), B分店代码 = "S1",
                    B单据类型 = "1", FGC_CreateDate = now },
            }).ExecuteCommandAsync();
            await _history.Insertable(new[]
            {
                new SalesOrderDetailRecord { ID = 1, B销售单号 = "VALID", B结账日期 = date,
                    B结账时间 = TimeSpan.FromHours(9), B分店代码 = "S1", B产品编号 = "P1",
                    B合计金额 = 4m, B数量 = 1m, FGC_CreateDate = now },
                new SalesOrderDetailRecord { ID = 2, B销售单号 = "VALID", B结账日期 = date,
                    B结账时间 = TimeSpan.FromHours(10), B分店代码 = "S1", B产品编号 = "P1",
                    B合计金额 = 6m, B数量 = 1m, FGC_CreateDate = now },
                new SalesOrderDetailRecord { ID = 3, B销售单号 = "TYPE2", B结账日期 = date,
                    B结账时间 = TimeSpan.FromHours(10), B分店代码 = "S1", B产品编号 = "P2",
                    B合计金额 = 999m, B数量 = 99m, FGC_CreateDate = now },
                new SalesOrderDetailRecord { ID = 4, B销售单号 = "NO-PRODUCT", B结账日期 = date,
                    B结账时间 = TimeSpan.FromHours(11), B分店代码 = "S1", B产品编号 = "",
                    B合计金额 = 0m, B数量 = 0m, FGC_CreateDate = now },
            }).ExecuteCommandAsync();
        }

        internal async Task DrainAsync()
        {
            for (var i = 0; i < 10; i++)
                if (!await Service.RunOneAsync(CancellationToken.None)) return;
            Assert.Fail("分时回填状态未在有限步骤内稳定");
        }

        internal Task<List<HourlySalesStatistic>> ReadViewAsync(DateTime date) =>
            Db.Queryable<HourlySalesStatistic>().AS("HourlySalesReadStatistic")
                .Where(row => row.Date == date.Date).ToListAsync();

        public async ValueTask DisposeAsync()
        {
            Db.Dispose();
            _posm.Dispose();
            _history.Dispose();
            SqlConnection.ClearAllPools();
            await _master.Ado.ExecuteCommandAsync(
                $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await _master.Ado.ExecuteCommandAsync($"DROP DATABASE [{_database}]");
            _master.Dispose();
        }

        private static T Inject<T>(ISqlSugarClient db)
        {
            var value = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            typeof(T).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(value, db);
            return value;
        }

        private static ConnectionConfig Config(string connection) => new()
        {
            ConnectionString = connection,
            DbType = DbType.SqlServer,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
            MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = true },
        };

        internal static SqlSugarClient Client(string connection) => new(Config(connection));
    }
}
