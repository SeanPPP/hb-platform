using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class InstallmentOrderSchemaMigratorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"installment-schema-{Guid.NewGuid():N}.db"
    );
    private readonly ISqlSugarClient _db;

    public InstallmentOrderSchemaMigratorTests()
    {
        _db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = $"DataSource={_dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            }
        );
    }

    [Fact]
    public async Task EnsureAsync_空数据库重复执行_创建三张分期表和生命周期列()
    {
        var migratorType = typeof(BlazorApp.Api.Data.StartupSchemaMigrator).Assembly.GetType(
            "BlazorApp.Api.Data.InstallmentOrderSchemaMigrator"
        );
        Assert.NotNull(migratorType);
        var ensure = migratorType!.GetMethod(
            "EnsureAsync",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(ISqlSugarClient), typeof(ILogger)],
            modifiers: null
        );
        Assert.NotNull(ensure);

        await InvokeEnsureAsync(ensure!);
        await InvokeEnsureAsync(ensure!);

        Assert.True(_db.DbMaintenance.IsAnyTable("InstallmentOrder", false));
        Assert.True(_db.DbMaintenance.IsAnyTable("InstallmentOrderLine", false));
        Assert.True(_db.DbMaintenance.IsAnyTable("InstallmentPayment", false));

        var columns = _db.Ado.GetDataTable("PRAGMA table_info(InstallmentOrder)")
            .Rows.Cast<System.Data.DataRow>()
            .Select(row => Convert.ToString(row["name"]))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("PickedUpAt", columns);
        Assert.Contains("PickedUpBy", columns);
        Assert.Contains("PickupNote", columns);
        Assert.Contains("CancellationKind", columns);
        Assert.Contains("CancelledAt", columns);
        Assert.Contains("CancelledBy", columns);
        Assert.Contains("CancellationReason", columns);
        Assert.Contains("CancellationIdempotencyKey", columns);

        AssertIndexColumns(
            "InstallmentOrderLine",
            "IX_InstallmentOrderLine_InstallmentGuid",
            "InstallmentGuid"
        );
        AssertIndexColumns(
            "InstallmentPayment",
            "IX_InstallmentPayment_InstallmentGuid_RecordedAt",
            "InstallmentGuid",
            "RecordedAt"
        );
        AssertIndexColumns(
            "InstallmentOrder",
            "IX_InstallmentOrder_StoreCode_CreatedAt_InstallmentGuid",
            "StoreCode",
            "CreatedAt",
            "InstallmentGuid"
        );
    }

    [Fact]
    public async Task StartupSchemaMigrator和Program_启动时接入POSM分期迁移()
    {
        var repositoryRoot = FindRepositoryRoot();
        var startupSource = await File.ReadAllTextAsync(
            Path.Combine(repositoryRoot, "services/backend/BlazorApp.Api/Data/StartupSchemaMigrator.cs")
        );
        var programSource = await File.ReadAllTextAsync(
            Path.Combine(repositoryRoot, "services/backend/BlazorApp.Api/Program.cs")
        );

        Assert.Contains("public static Task EnsurePosmAsync", startupSource, StringComparison.Ordinal);
        Assert.Contains("InstallmentOrderSchemaMigrator.EnsureAsync(posmDb, logger)", startupSource, StringComparison.Ordinal);
        Assert.Contains("await StartupSchemaMigrator.EnsurePosmAsync(posmDbContext.Db, app.Logger);", programSource, StringComparison.Ordinal);
        Assert.True(
            // 主库 CodeFirst 可因其他迁移前置；POSM 分期结构只需在 Web 服务启动前完成。
            programSource.IndexOf("StartupSchemaMigrator.EnsurePosmAsync", StringComparison.Ordinal)
            < programSource.IndexOf("app.Run();", StringComparison.Ordinal),
            "POSM 分期表必须在应用开始服务前完成初始化"
        );
    }

    [Theory]
    [InlineData("PickedUpAt", "datetime2")]
    [InlineData("PickedUpBy", "nvarchar(100)")]
    [InlineData("PickupNote", "nvarchar(500)")]
    [InlineData("CancellationKind", "int")]
    [InlineData("CancelledAt", "datetime2")]
    [InlineData("CancelledBy", "nvarchar(100)")]
    [InlineData("CancellationReason", "nvarchar(500)")]
    [InlineData("CancellationIdempotencyKey", "nvarchar(100)")]
    public async Task SQLServer兼容迁移_生命周期列统一修正为可空(string column, string sqlType)
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(FindRepositoryRoot(), "services/backend/BlazorApp.Api/Data/InstallmentOrderSchemaMigrator.cs")
        );
        var normalized = Regex.Replace(source, @"\s+", " ");

        Assert.Contains(
            $"AND [name] = N'{column}' AND [is_nullable] = 0",
            normalized,
            StringComparison.Ordinal
        );
        Assert.Contains(
            $"ALTER TABLE [dbo].[InstallmentOrder] ALTER COLUMN [{column}] {sqlType} NULL;",
            normalized,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task SQLServer兼容迁移_卡交易JSON为旧varchar时也转为nvarchar()
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(FindRepositoryRoot(), "services/backend/BlazorApp.Api/Data/InstallmentOrderSchemaMigrator.cs")
        );
        var normalized = Regex.Replace(source, @"\s+", " ");

        Assert.Contains(
            "AND ([system_type_id] <> TYPE_ID(N'nvarchar') OR [max_length] <> -1 OR [is_nullable] = 0)",
            normalized,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ALTER TABLE [dbo].[InstallmentPayment] ALTER COLUMN [CardTransactionsJson] nvarchar(max) NULL;",
            normalized,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("IX_InstallmentOrderLine_InstallmentGuid", "[dbo].[InstallmentOrderLine]([InstallmentGuid])")]
    [InlineData("IX_InstallmentPayment_InstallmentGuid_RecordedAt", "[dbo].[InstallmentPayment]([InstallmentGuid], [RecordedAt])")]
    [InlineData("IX_InstallmentOrder_StoreCode_CreatedAt_InstallmentGuid", "[dbo].[InstallmentOrder]([StoreCode], [CreatedAt], [InstallmentGuid])")]
    public async Task SQLServer兼容迁移_分期查询索引不存在时才创建(string indexName, string tableAndColumns)
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(FindRepositoryRoot(), "services/backend/BlazorApp.Api/Data/InstallmentOrderSchemaMigrator.cs")
        );
        var normalized = Regex.Replace(source, @"\s+", " ");

        Assert.Contains(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'{indexName}'",
            normalized,
            StringComparison.Ordinal
        );
        Assert.Contains(
            $"CREATE INDEX [{indexName}] ON {tableAndColumns};",
            normalized,
            StringComparison.Ordinal
        );
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "services", "backend")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位仓库根目录");
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private async Task InvokeEnsureAsync(MethodInfo ensure)
    {
        var task = Assert.IsAssignableFrom<Task>(
            ensure.Invoke(null, [_db, NullLogger.Instance])
        );
        await task;
    }

    private void AssertIndexColumns(string tableName, string indexName, params string[] expectedColumns)
    {
        var indexes = _db.Ado.GetDataTable($"PRAGMA index_list('{tableName}')")
            .Rows.Cast<System.Data.DataRow>()
            .Select(row => Convert.ToString(row["name"]))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(indexName, indexes);

        var actualColumns = _db.Ado.GetDataTable($"PRAGMA index_info('{indexName}')")
            .Rows.Cast<System.Data.DataRow>()
            .OrderBy(row => Convert.ToInt32(row["seqno"]))
            .Select(row => Convert.ToString(row["name"]))
            .ToArray();
        Assert.Equal(expectedColumns, actualColumns);
    }
}
