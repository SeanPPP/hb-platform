using BlazorApp.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ContainerDetailCollaborationSchemaMigratorTests
{
    [Fact]
    public void SQLServer签名_验证两表关键列主键索引与只追加触发器()
    {
        var sql = ContainerDetailCollaborationSchemaMigrator.VerifySql;
        Assert.Contains("ContainerDetailEditLease", sql, StringComparison.Ordinal);
        Assert.Contains("ContainerDetailFieldOverrideAudit", sql, StringComparison.Ordinal);
        Assert.Contains("LeaseKey", sql, StringComparison.Ordinal);
        Assert.Contains("UserName", sql, StringComparison.Ordinal);
        Assert.Contains("ConfirmationToken", sql, StringComparison.Ordinal);
        Assert.Contains("ActorName", sql, StringComparison.Ordinal);
        Assert.Contains("system_type_id", sql, StringComparison.Ordinal);
        Assert.Contains("max_length", sql, StringComparison.Ordinal);
        Assert.Contains("expected.precision", sql, StringComparison.Ordinal);
        Assert.Contains("CAST(27 AS tinyint)", sql, StringComparison.Ordinal);
        Assert.Contains("is_nullable", sql, StringComparison.Ordinal);
        Assert.Contains("datetime2(7)", File.ReadAllText(FindMigratorPath()), StringComparison.Ordinal);
        Assert.Contains("IX_ContainerDetailEditLease_Container_Expires", sql, StringComparison.Ordinal);
        Assert.Contains("IX_ContainerDetailFieldOverrideAudit_Container_Occurred", sql, StringComparison.Ordinal);
        Assert.Contains("ic.is_descending_key=1", sql, StringComparison.Ordinal);
        Assert.Contains("i.is_disabled = 0", sql, StringComparison.Ordinal);
        Assert.Contains("i.has_filter = 0", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(1) FROM sys.index_columns AS keysOnly", sql, StringComparison.Ordinal);
        Assert.Contains("TR_ContainerDetailFieldOverrideAudit_AppendOnly", sql, StringComparison.Ordinal);
        Assert.Contains("is_instead_of_trigger = 1", sql, StringComparison.Ordinal);
        Assert.Contains("COALESCE(OBJECT_DEFINITION", sql, StringComparison.Ordinal);
        Assert.Contains("@NormalizedAppendOnlyTrigger", sql, StringComparison.Ordinal);
        Assert.Contains("COLLATE Latin1_General_100_BIN2", sql, StringComparison.Ordinal);
        Assert.Contains("createoraltertrigger[dbo].[tr_containerdetailfieldoverrideaudit_appendonly]", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("NOT LIKE N'%INSTEAD OF UPDATE, DELETE%'", sql, StringComparison.Ordinal);
    }

    private static string FindMigratorPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "services", "backend", "BlazorApp.Api", "Data", "ContainerDetailCollaborationSchemaMigrator.cs");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("无法定位协作迁移器源码");
    }

    [Fact]
    public async Task SQLite_启动迁移必须保持既有EarlyReturn且不创建协作表()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = connection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );

        await ContainerDetailCollaborationSchemaMigrator.EnsureAsync(db, NullLogger.Instance);

        var tableCount = await db.Ado.GetIntAsync(
            "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' "
                + "AND name IN ('ContainerDetailEditLease', 'ContainerDetailFieldOverrideAudit')"
        );
        Assert.Equal(0, tableCount);
    }
}
