using BlazorApp.Api.Data.SchemaMigrations;
using BlazorApp.Api.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesDetailQueryProjectionTests
{
    [Fact]
    public void SQL契约_状态映射授权分店和历史别名均被投影覆盖()
    {
        var sql = SalesDetailQueryProjection.BuildRefreshDaySql("POSM-test]");

        Assert.Equal(1, SalesDetailQueryProjection.SchemaVersion);
        Assert.Contains("[POSM-test]]].[dbo].[posm_product_supplier_mapping]", sql);
        Assert.Contains("[SourceBranchCode]", sql);
        Assert.Contains("FOR JSON PATH, INCLUDE_NULL_VALUES", sql);
        Assert.Contains("[SourceProductVersion]", sql);
        Assert.Contains("[SourceLastAggregatedAtUtc]", sql);
        Assert.Contains("[SourceCompletedAtUtc]", sql);
        Assert.Contains("[SourceJobId]", sql);
        Assert.Contains("[MappingHasFanout]", sql);
        Assert.Contains("[SalesDetailQueryMappingUse]", sql);
        Assert.Contains("IF @sdpMappingHasFanout = 0", sql);
        Assert.Contains("DROP TABLE #SalesDetailProjectionRows", sql);
        Assert.Contains("@LockOwner = N'Transaction'", sql);
        Assert.DoesNotContain("CREATE TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 显式Schema_固定三表关键类型和索引()
    {
        var sql = SalesDetailQueryProjection.CreateSchemaSql;

        Assert.Same(SalesDetailQueryProjectionSchema.ApplySql, sql);
        Assert.Contains("[SourceJobId] uniqueidentifier NULL", sql);
        Assert.Contains("[SourceBranchCode] nvarchar(50) NOT NULL", sql);
        Assert.Contains("[MappingHasFanout] bit NOT NULL", sql);
        Assert.Contains("DEFAULT (1)", sql);
        Assert.Contains("[PK_SalesDetailQueryMappingUse] PRIMARY KEY CLUSTERED ([Date], [ProductCode])", sql);
        Assert.Contains("SET [MappingHasFanout] = 1", sql);
        Assert.Contains("CREATE UNIQUE CLUSTERED INDEX [CUX_SalesDetailQueryProductAlias]", sql);
        Assert.Contains("[Date], [SourceBranchCode]", sql);
    }

    [Fact]
    public void 维护配置_只接受完整且不超过366天的精确日期范围()
    {
        var valid = BuildMaintenanceConfiguration("2025-09-09", "2026-09-09");
        Assert.True(SalesDetailQueryProjectionMaintenanceRunner.TryReadSettings(valid, out var settings));
        Assert.Equal(366, (settings.EndDate - settings.StartDate).Days + 1);

        Assert.False(SalesDetailQueryProjectionMaintenanceRunner.TryReadSettings(
            BuildMaintenanceConfiguration("2025-09-08", "2026-09-09"), out _));
        Assert.False(SalesDetailQueryProjectionMaintenanceRunner.TryReadSettings(
            BuildMaintenanceConfiguration("09/09/2025", "2026-09-09"), out _));
        Assert.False(SalesDetailQueryProjectionMaintenanceRunner.TryReadSettings(
            BuildMaintenanceConfiguration("2026-09-09", "2026-09-08"), out _));
        var missingTarget = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["SalesDetailProjection:StartDate"] = "2026-09-09",
                ["SalesDetailProjection:EndDate"] = "2026-09-09",
            }).Build();
        Assert.False(SalesDetailQueryProjectionMaintenanceRunner.TryReadSettings(missingTarget, out _));
    }

    private static IConfiguration BuildMaintenanceConfiguration(string start, string end) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SalesDetailProjection:StartDate"] = start,
            ["SalesDetailProjection:EndDate"] = end,
            ["SalesDetailProjection:ExpectedDatabase"] = "HBweb",
            ["SalesDetailProjection:ExpectedServer"] = "test-server",
            ["ConnectionStrings:DefaultConnection"] =
                "Server=test-server;Database=HBweb;User Id=test;Password=test;Encrypt=False",
            ["ConnectionStrings:HBPOSMConnection"] =
                "Server=test-server;Database=POSM;User Id=test;Password=test;Encrypt=False",
        }).Build();
}

public sealed class SalesDetailProjectionSqlServerFactAttribute : FactAttribute
{
    internal const string EnvironmentVariable = "HB_TEST_SQLSERVER_CONNECTION";

    public SalesDetailProjectionSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
            Skip = $"未配置 {EnvironmentVariable}，跳过隔离 SQL Server 投影测试。";
    }
}

[Trait("Category", "SQL")]
public sealed class SalesDetailQueryProjectionSqlServerTests
{
    [SalesDetailProjectionSqlServerFact]
    public async Task 刷新日_同份映射签名重复语义原始授权码空日和撤销均正确()
    {
        var root = Environment.GetEnvironmentVariable(
            SalesDetailProjectionSqlServerFactAttribute.EnvironmentVariable)!;
        var suffix = Guid.NewGuid().ToString("N");
        var mainDatabase = $"HB_SDP_MAIN_{suffix}";
        var posmDatabase = $"HB_SDP_POSM_{suffix}";
        var rootBuilder = new SqlConnectionStringBuilder(root) { InitialCatalog = "master" };

        await using var rootConnection = new SqlConnection(rootBuilder.ConnectionString);
        await rootConnection.OpenAsync();
        try
        {
            await ExecuteAsync(rootConnection, $"""
CREATE DATABASE [{mainDatabase}];
ALTER DATABASE [{mainDatabase}] SET ALLOW_SNAPSHOT_ISOLATION ON;
CREATE DATABASE [{posmDatabase}];
ALTER DATABASE [{posmDatabase}] SET ALLOW_SNAPSHOT_ISOLATION ON;
""");
            var mainBuilder = new SqlConnectionStringBuilder(root) { InitialCatalog = mainDatabase };
            var posmBuilder = new SqlConnectionStringBuilder(root) { InitialCatalog = posmDatabase };
            await using var main = new SqlConnection(mainBuilder.ConnectionString);
            await using var posm = new SqlConnection(posmBuilder.ConnectionString);
            await main.OpenAsync();
            await posm.OpenAsync();

            await ExecuteAsync(main, """
CREATE TABLE dbo.ChinaSupplier (SupplierCode nvarchar(50) NULL);
CREATE TABLE dbo.SalesStatisticRefreshState
(
 StatisticType nvarchar(80) NOT NULL, [Date] datetime2 NOT NULL, [Status] nvarchar(20) NOT NULL,
 SourceProductVersion nvarchar(128) NULL, LastAggregatedAtUtc datetime2 NULL,
 CompletedAtUtc datetime2 NULL, JobId uniqueidentifier NULL
);
CREATE TABLE dbo.ProductStoreDailySalesStatistic
(
 [Date] datetime2 NOT NULL, BranchCode nvarchar(50) NOT NULL, SupplierCode nvarchar(50) NOT NULL,
 ProductCode nvarchar(50) NOT NULL, ProductName nvarchar(255) NULL, Barcode nvarchar(100) NULL,
 TotalQuantity int NOT NULL, TotalAmount decimal(18,4) NOT NULL, OrderCount int NOT NULL,
 GrossProfit decimal(18,4) NULL, TotalCost decimal(18,4) NULL
);
""");
            await ExecuteAsync(posm, """
CREATE TABLE dbo.posm_product_supplier_mapping
(
 Id int IDENTITY PRIMARY KEY, ProductCode nvarchar(50) NULL, ChinaSupplierCode nvarchar(50) NULL,
 LocalSupplierCode nvarchar(50) NOT NULL, IsDeleted bit NOT NULL
);
""");
            await ExecuteAsync(main, SalesDetailQueryProjection.CreateSchemaSql);
            await ExecuteAsync(main, SalesDetailQueryProjectionSchema.VerifySql);
            await ExecuteAsync(main, $"""
CREATE TABLE dbo.{SqlServerSchemaMigrationRuntime.MainHistoryTable}
 (MigrationId nvarchar(160) NOT NULL PRIMARY KEY, AppliedAtUtc datetime2 NOT NULL, ApplicationVersion nvarchar(64) NOT NULL);
INSERT dbo.{SqlServerSchemaMigrationRuntime.MainHistoryTable}
 VALUES (N'{SchemaMigrationCoordinator.SalesDetailQueryProjectionMigrationId}', SYSUTCDATETIME(), N'test'),
        (N'{SchemaMigrationCoordinator.SalesDetailQueryMappingUseMigrationId}', SYSUTCDATETIME(), N'test');
""");

            var jobId = Guid.NewGuid();
            await ExecuteAsync(main, $"""
INSERT dbo.ChinaSupplier VALUES (N'C2'), (N'C2');
INSERT dbo.SalesStatisticRefreshState VALUES
 (N'ProductStoreDaily', '2026-09-08', N'Fresh', N'v1', '2026-09-09T01:00:00', '2026-09-09T01:01:00', '{jobId}'),
 (N'ProductStoreDaily', '2026-09-07', N'Fresh', N'v0', '2026-09-08T01:00:00', '2026-09-08T01:01:00', NULL);
INSERT dbo.ProductStoreDailySalesStatistic VALUES
 ('2026-09-08', N' B1', N'200', N' P1 ', N'旧名;:', N'01', 2, 10, 1, 4, 6),
 ('2026-09-08', N'B1', N'C2', N'P2', NULL, NULL, 3, 15, 2, NULL, NULL);
""");
            await ExecuteAsync(posm, """
INSERT dbo.posm_product_supplier_mapping (ProductCode, ChinaSupplierCode, LocalSupplierCode, IsDeleted) VALUES
 (N'P1', N'C1', N'200', 0), (N'P1', N'C1', N'200', 0),
 (N'control:; ', N'line'+NCHAR(10)+N'break', N'200', 0), (N'ignored', N'X', N'201', 0);
""");

            await RefreshAsync(main, posmDatabase, new DateTime(2026, 9, 8));

            await using (var command = main.CreateCommand())
            {
                command.CommandText = $"""
SELECT COUNT_BIG(*), SUM(Revenue), SUM(StatisticRowCount),
       SUM(CASE WHEN SourceBranchCode=N' B1' AND BranchCode=N'B1' THEN 1 ELSE 0 END)
FROM dbo.SalesDetailQueryDaily WHERE [Date]='2026-09-08';
SELECT SourceProductVersion, SourceJobId, MappingVersion, MappingHasFanout,
       {SalesDetailQueryProjection.BuildMappingSignatureSql(posmDatabase)} AS ExpectedMappingVersion
FROM dbo.SalesDetailQueryProjectionState WHERE [Date]='2026-09-08';
SELECT COUNT_BIG(*) FROM dbo.SalesDetailQueryProductAlias
 WHERE ProductCode=N' P1 ' AND ProductName=N'旧名;:' AND Barcode=N'01';
""";
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(2L, reader.GetInt64(0));
                Assert.Equal(35m, reader.GetDecimal(1));
                Assert.Equal(3m, Convert.ToDecimal(reader.GetValue(2)));
                Assert.Equal(1, reader.GetInt32(3));
                Assert.True(await reader.NextResultAsync());
                Assert.True(await reader.ReadAsync());
                Assert.Equal("v1", reader.GetString(0));
                Assert.Equal(jobId, reader.GetGuid(1));
                Assert.True(reader.GetBoolean(3));
                Assert.Equal(reader.GetString(4), reader.GetString(2));
                Assert.True(await reader.NextResultAsync());
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1L, reader.GetInt64(0));
            }
            Assert.Equal(0, await ScalarIntAsync(main,
                "SELECT COUNT(*) FROM dbo.SalesDetailQueryMappingUse WHERE [Date]='2026-09-08';"));

            // 模拟旧部署只有覆盖状态、没有相关映射证明表；显式迁移必须保守降级旧覆盖。
            await ExecuteAsync(main, """
UPDATE dbo.SalesDetailQueryProjectionState SET MappingHasFanout=0 WHERE [Date]='2026-09-08';
DROP TABLE dbo.SalesDetailQueryMappingUse;
""");
            await ExecuteAsync(main, SalesDetailQueryProjection.CreateSchemaSql);
            Assert.Equal(1, await ScalarIntAsync(main,
                "SELECT CONVERT(int, MappingHasFanout) FROM dbo.SalesDetailQueryProjectionState WHERE [Date]='2026-09-08';"));

            // 去除 fanout 后，同品多分店只留一份证明；未映射商品以 NULL ChinaSupplierCode 留证。
            await ExecuteAsync(posm, """
DELETE FROM dbo.posm_product_supplier_mapping
WHERE Id=(SELECT MAX(Id) FROM dbo.posm_product_supplier_mapping WHERE ProductCode=N'P1');
""");
            await ExecuteAsync(main, """
INSERT dbo.ProductStoreDailySalesStatistic VALUES
 ('2026-09-08', N'B2', N'200', N' P1 ', N'新分店名', N'01', 1, 5, 1, 2, 3),
 ('2026-09-08', N'B1', N' 200 ', N' P3 ', N'未映射', N'03', 1, 7, 1, NULL, NULL);
""");
            await RefreshAsync(main, posmDatabase, new DateTime(2026, 9, 8));
            Assert.Equal(2, await ScalarIntAsync(main, """
SELECT COUNT(*) FROM dbo.SalesDetailQueryMappingUse WHERE [Date]='2026-09-08';
"""));
            Assert.Equal(1, await ScalarIntAsync(main, """
SELECT COUNT(*) FROM dbo.SalesDetailQueryMappingUse
WHERE [Date]='2026-09-08' AND ProductCode=N'P1' AND ChinaSupplierCode=N'C1';
"""));
            Assert.Equal(1, await ScalarIntAsync(main, """
SELECT COUNT(*) FROM dbo.SalesDetailQueryMappingUse
WHERE [Date]='2026-09-08' AND ProductCode=N'P3' AND ChinaSupplierCode IS NULL;
"""));
            await RefreshAsync(main, posmDatabase, new DateTime(2026, 9, 8));
            Assert.Equal(2, await ScalarIntAsync(main,
                "SELECT COUNT(*) FROM dbo.SalesDetailQueryMappingUse WHERE [Date]='2026-09-08';"));

            // 同一连接第二次运行证明临时表已清理；完整空日期仍发布覆盖。
            await RefreshAsync(main, posmDatabase, new DateTime(2026, 9, 7));
            Assert.Equal(1, await ScalarIntAsync(main,
                "SELECT COUNT(*) FROM dbo.SalesDetailQueryProjectionState WHERE [Date]='2026-09-07';"));
            Assert.Equal(0, await ScalarIntAsync(main,
                "SELECT COUNT(*) FROM dbo.SalesDetailQueryDaily WHERE [Date]='2026-09-07';"));

            var actualServer = await ScalarStringAsync(main, "SELECT CONVERT(nvarchar(128), @@SERVERNAME);");
            var maintenanceConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SalesDetailProjection:StartDate"] = "2026-09-07",
                    ["SalesDetailProjection:EndDate"] = "2026-09-08",
                    ["SalesDetailProjection:ExpectedDatabase"] = mainDatabase,
                    ["SalesDetailProjection:ExpectedServer"] = actualServer,
                    ["ConnectionStrings:DefaultConnection"] = mainBuilder.ConnectionString,
                    ["ConnectionStrings:HBPOSMConnection"] = posmBuilder.ConnectionString,
                    ["Database:CommandTimeoutSeconds"] = "30",
                }).Build();
            var runner = new SalesDetailQueryProjectionMaintenanceRunner(
                maintenanceConfiguration,
                NullLogger<SalesDetailQueryProjectionMaintenanceRunner>.Instance);
            var wrongTargetConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(maintenanceConfiguration.AsEnumerable()
                    .ToDictionary(item => item.Key, item => item.Key == "SalesDetailProjection:ExpectedDatabase"
                        ? "WRONG_DATABASE" : item.Value))
                .Build();
            var wrongTarget = await new SalesDetailQueryProjectionMaintenanceRunner(
                wrongTargetConfiguration,
                NullLogger<SalesDetailQueryProjectionMaintenanceRunner>.Instance)
                .RunAsync(checkOnly: true, CancellationToken.None);
            Assert.False(wrongTarget.Success);
            Assert.Equal(SchemaDiagnosticCodes.SalesDetailProjectionTargetMismatch, wrongTarget.DiagnosticCode);
            await ExecuteAsync(main,
                "DELETE dbo.SalesDetailQueryDaily; DELETE dbo.SalesDetailQueryProjectionState;");
            var backfill = await runner.RunAsync(checkOnly: false, CancellationToken.None);
            Assert.True(backfill.Success, backfill.DiagnosticCode);
            Assert.True((await runner.RunAsync(checkOnly: true, CancellationToken.None)).Success);
            await ExecuteAsync(main,
                "UPDATE dbo.SalesDetailQueryProjectionState SET MappingVersion=REPLICATE('0',64) WHERE [Date]='2026-09-07';");
            var gap = await runner.RunAsync(checkOnly: true, CancellationToken.None);
            Assert.False(gap.Success);
            Assert.Equal(SchemaDiagnosticCodes.SalesDetailProjectionCoverageMissing, gap.DiagnosticCode);
            Assert.True((await runner.RunAsync(checkOnly: false, CancellationToken.None)).Success);

            await ExecuteAsync(main,
                "UPDATE dbo.SalesStatisticRefreshState SET [Status]=N'Failed' WHERE [Date]='2026-09-07';");
            Assert.Equal(
                SchemaDiagnosticCodes.SalesDetailProjectionCoverageMissing,
                (await runner.RunAsync(checkOnly: true, CancellationToken.None)).DiagnosticCode);
            await ExecuteAsync(main,
                "UPDATE dbo.SalesStatisticRefreshState SET [Status]=N'Fresh', SourceProductVersion=NULL WHERE [Date]='2026-09-07';");
            Assert.Equal(
                SchemaDiagnosticCodes.SalesDetailProjectionCoverageMissing,
                (await runner.RunAsync(checkOnly: true, CancellationToken.None)).DiagnosticCode);
            await ExecuteAsync(main,
                "UPDATE dbo.SalesStatisticRefreshState SET SourceProductVersion=N'v0', LastAggregatedAtUtc=NULL WHERE [Date]='2026-09-07';");
            Assert.Equal(
                SchemaDiagnosticCodes.SalesDetailProjectionCoverageMissing,
                (await runner.RunAsync(checkOnly: false, CancellationToken.None)).DiagnosticCode);
            await ExecuteAsync(main,
                "UPDATE dbo.SalesStatisticRefreshState SET LastAggregatedAtUtc='2026-09-08T01:00:00' WHERE [Date]='2026-09-07';");
            Assert.True((await runner.RunAsync(checkOnly: false, CancellationToken.None)).Success);

            await ExecuteAsync(main,
                "DELETE dbo.SalesStatisticRefreshState WHERE [Date]='2026-09-07';");
            Assert.Equal(
                SchemaDiagnosticCodes.SalesDetailProjectionCoverageMissing,
                (await runner.RunAsync(checkOnly: true, CancellationToken.None)).DiagnosticCode);
            await ExecuteAsync(main, """
INSERT dbo.SalesStatisticRefreshState VALUES
 (N'ProductStoreDaily', '2026-09-07', N'Fresh', N'v0', '2026-09-08T01:00:00', '2026-09-08T01:01:00', NULL);
""");
            Assert.True((await runner.RunAsync(checkOnly: false, CancellationToken.None)).Success);

            var emptyRangeValues = maintenanceConfiguration.AsEnumerable()
                .ToDictionary(item => item.Key, item => item.Value);
            emptyRangeValues["SalesDetailProjection:StartDate"] = "2026-09-09";
            emptyRangeValues["SalesDetailProjection:EndDate"] = "2026-09-09";
            var emptyRangeRunner = new SalesDetailQueryProjectionMaintenanceRunner(
                new ConfigurationBuilder().AddInMemoryCollection(emptyRangeValues).Build(),
                NullLogger<SalesDetailQueryProjectionMaintenanceRunner>.Instance);
            Assert.Equal(
                SchemaDiagnosticCodes.SalesDetailProjectionCoverageMissing,
                (await emptyRangeRunner.RunAsync(checkOnly: true, CancellationToken.None)).DiagnosticCode);

            await ExecuteAsync(main, "UPDATE dbo.SalesStatisticRefreshState SET [Status]=N'Failed' WHERE [Date]='2026-09-08';");
            await RefreshAsync(main, posmDatabase, new DateTime(2026, 9, 8));
            Assert.Equal(0, await ScalarIntAsync(main,
                "SELECT COUNT(*) FROM dbo.SalesDetailQueryProjectionState WHERE [Date]='2026-09-08';"));
            Assert.Equal(0, await ScalarIntAsync(main,
                "SELECT COUNT(*) FROM dbo.SalesDetailQueryDaily WHERE [Date]='2026-09-08';"));
            Assert.Equal(0, await ScalarIntAsync(main,
                "SELECT COUNT(*) FROM dbo.SalesDetailQueryMappingUse WHERE [Date]='2026-09-08';"));
            Assert.Equal(1, await ScalarIntAsync(main,
                "SELECT COUNT(*) FROM dbo.SalesDetailQueryProductAlias WHERE ProductCode=N' P1 ' AND ProductName=N'旧名;:' AND Barcode=N'01';"));
        }
        finally
        {
            await ExecuteAsync(rootConnection, $"""
IF DB_ID(N'{mainDatabase}') IS NOT NULL BEGIN ALTER DATABASE [{mainDatabase}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{mainDatabase}]; END;
IF DB_ID(N'{posmDatabase}') IS NOT NULL BEGIN ALTER DATABASE [{posmDatabase}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{posmDatabase}]; END;
""");
        }
    }

    private static async Task RefreshAsync(SqlConnection connection, string posmDatabase, DateTime date)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)transaction;
        command.CommandText = SalesDetailQueryProjection.BuildRefreshDaySql(posmDatabase);
        command.Parameters.Add(new SqlParameter("@sdpDate", date));
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarIntAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ScalarStringAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }
}
