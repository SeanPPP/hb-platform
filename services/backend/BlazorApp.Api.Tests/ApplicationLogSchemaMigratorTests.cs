using BlazorApp.Api.Services.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ApplicationLogSchemaMigratorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"application-log-schema-{Guid.NewGuid():N}.db"
    );
    private readonly ISqlSugarClient _db;

    public ApplicationLogSchemaMigratorTests()
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
        _db.Ado.ExecuteCommand(
            "CREATE TABLE ApplicationLog (Id TEXT PRIMARY KEY, ProjectCode TEXT NOT NULL, TimestampUtc TEXT NOT NULL, InstanceId TEXT NULL)"
        );
    }

    [Fact]
    public async Task EnsureAsync_旧表执行两次_补齐Wpf列和过滤唯一索引()
    {
        await ApplicationLogSchemaMigrator.EnsureAsync(_db, NullLogger.Instance);
        await ApplicationLogSchemaMigrator.EnsureAsync(_db, NullLogger.Instance);

        var columnTable = _db.Ado.GetDataTable("PRAGMA table_info(ApplicationLog)");
        var columnNames = columnTable
            .Rows.Cast<System.Data.DataRow>()
            .Select(row => Convert.ToString(row["name"]))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ClientEventId", columnNames);
        Assert.Contains("StoreCode", columnNames);
        Assert.Contains("DeviceCode", columnNames);
        Assert.Contains("AppVersion", columnNames);

        var indexTable = _db.Ado.GetDataTable("PRAGMA index_list(ApplicationLog)");
        var indexNames = indexTable
            .Rows.Cast<System.Data.DataRow>()
            .Select(row => Convert.ToString(row["name"]))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("IX_ApplicationLog_ProjectCode_ClientEventId", indexNames);

        var eventId = Guid.NewGuid().ToString();
        _db.Ado.ExecuteCommand(
            "INSERT INTO ApplicationLog (Id, ProjectCode, TimestampUtc, ClientEventId) VALUES (@Id, @ProjectCode, @TimestampUtc, @ClientEventId)",
            new SugarParameter("@Id", Guid.NewGuid().ToString()),
            new SugarParameter("@ProjectCode", "hbpos_win"),
            new SugarParameter("@TimestampUtc", DateTime.UtcNow),
            new SugarParameter("@ClientEventId", eventId)
        );
        Assert.ThrowsAny<Exception>(() =>
            _db.Ado.ExecuteCommand(
                "INSERT INTO ApplicationLog (Id, ProjectCode, TimestampUtc, ClientEventId) VALUES (@Id, @ProjectCode, @TimestampUtc, @ClientEventId)",
                new SugarParameter("@Id", Guid.NewGuid().ToString()),
                new SugarParameter("@ProjectCode", "hbpos_win"),
                new SugarParameter("@TimestampUtc", DateTime.UtcNow),
                new SugarParameter("@ClientEventId", eventId)
            )
        );
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }
}
