using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>真实 SQL Server 验证必须显式提供本机专用连接，避免普通测试误连任何共享数据库。</summary>
public sealed class FaceAttendanceSchemaSqlServerFactAttribute : FactAttribute
{
    public FaceAttendanceSchemaSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FaceAttendanceSchemaSqlServerTests.ConnectionEnvironmentVariable)))
            Skip = $"未配置 {FaceAttendanceSchemaSqlServerTests.ConnectionEnvironmentVariable}，跳过真实 SQL Server 人脸考勤结构验证。";
    }
}

[Trait("Category", "SQL")]
public sealed class FaceAttendanceSchemaSqlServerTests
{
    internal const string ConnectionEnvironmentVariable = "FACE_ATTENDANCE_SQLSERVER_TEST_CONNECTION";

    [FaceAttendanceSchemaSqlServerFact]
    public async Task EnsureAsync_真实SQLServer可重复执行并保证人脸事件约束与租约更新()
    {
        var baseConnection = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(baseConnection));
        var baseBuilder = new SqlConnectionStringBuilder(baseConnection!);
        // 这是破坏性测试的最后一道防线：只允许本机回环地址和随机测试库。
        Assert.True(IsLoopbackDataSource(baseBuilder.DataSource), "真实结构测试只能连接本机 Docker SQL Server。");

        var databaseName = $"HBface_schema_test_{Guid.NewGuid():N}";
        var masterConnection = WithDatabase(baseConnection!, "master");
        var databaseConnection = WithDatabase(baseConnection!, databaseName);
        await CreateDatabaseAsync(masterConnection, databaseName);
        try
        {
            using var db = CreateSqlServerClient(databaseConnection);
            // 生产表在本迁移之前已存在；这里只创建最小兼容表，验证补列和唯一关联索引。
            await ExecuteAsync(databaseConnection, "CREATE TABLE [dbo].[AttendancePunch]([Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY);");

            await FaceAttendanceSchemaMigrator.EnsureAsync(db, NullLogger.Instance);
            await FaceAttendanceSchemaMigrator.EnsureAsync(db, NullLogger.Instance);

            foreach (var table in new[] { "FaceAttendanceEnrollment", "FaceAttendanceEvent", "FaceAttendanceDeviceKey", "FaceAttendanceTimeAnchor", "FaceAttendanceRosterSnapshot", "AttendancePunch" })
                Assert.Equal(1, await ScalarAsync<int>(databaseConnection, "SELECT COUNT(1) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo') AND name=@name;", new SqlParameter("@name", table)));

            var eventColumns = await QueryStringsAsync(databaseConnection, "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=N'dbo' AND TABLE_NAME=N'FaceAttendanceEvent';");
            Assert.Subset(new HashSet<string>(eventColumns), new HashSet<string> { "EventGuid", "ImmutablePayloadHash", "Signature", "LeaseId", "LeaseExpiresAtUtc", "Status", "PunchGuid", "RetainUntilUtc" });
            var enrollmentColumns = await QueryStringsAsync(databaseConnection, "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=N'dbo' AND TABLE_NAME=N'FaceAttendanceEnrollment';");
            Assert.Contains("LastRequestHash", enrollmentColumns);
            var punchColumns = await QueryStringsAsync(databaseConnection, "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=N'dbo' AND TABLE_NAME=N'AttendancePunch';");
            Assert.Contains("FaceEventGuid", punchColumns);

            Assert.Equal(1, await ScalarAsync<int>(databaseConnection, """
                SELECT COUNT(1) FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
                JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                WHERE i.object_id=OBJECT_ID(N'[dbo].[FaceAttendanceEvent]') AND i.is_unique=1 AND c.name=N'EventGuid';
                """));
            Assert.Equal(1, await ScalarAsync<int>(databaseConnection, """
                SELECT COUNT(1) FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
                JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                WHERE i.object_id=OBJECT_ID(N'[dbo].[AttendancePunch]') AND i.is_unique=1 AND i.has_filter=1 AND c.name=N'FaceEventGuid';
                """));

            var now = DateTime.UtcNow;
            var eventGuid = $"event-{Guid.NewGuid():N}";
            await db.Insertable(NewEvent(eventGuid, now)).ExecuteCommandAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => db.Insertable(NewEvent(eventGuid, now)).ExecuteCommandAsync());

            await ExecuteAsync(databaseConnection, "INSERT INTO [dbo].[AttendancePunch]([FaceEventGuid]) VALUES(@eventGuid);", new SqlParameter("@eventGuid", eventGuid));
            await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(databaseConnection, "INSERT INTO [dbo].[AttendancePunch]([FaceEventGuid]) VALUES(@eventGuid);", new SqlParameter("@eventGuid", eventGuid)));

            var leaseId = Guid.NewGuid().ToString("N");
            var leaseUntil = now.AddMinutes(2);
            // 使用 worker 的条件更新形状，确认 SQL Server 中 lease、状态和重试计数可原子写入。
            var claimed = await db.Updateable<FaceAttendanceEvent>()
                .SetColumns(x => new FaceAttendanceEvent
                {
                    LeaseId = leaseId,
                    LeaseExpiresAtUtc = leaseUntil,
                    Status = "verifying",
                    AttemptCount = x.AttemptCount + 1,
                    UpdatedAtUtc = now,
                })
                .Where(x => x.EventGuid == eventGuid && x.Status == "queued" && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc < now))
                .ExecuteCommandAsync();
            Assert.Equal(1, claimed);
            var saved = await db.Queryable<FaceAttendanceEvent>().SingleAsync(x => x.EventGuid == eventGuid);
            Assert.Equal("verifying", saved.Status);
            Assert.Equal(leaseId, saved.LeaseId);
            Assert.Equal(1, saved.AttemptCount);
            // SqlSugar 的 SQL Server DateTime 参数会规范到毫秒，租约必须保持同一毫秒窗口而非比较 CLR tick。
            Assert.InRange(saved.LeaseExpiresAtUtc!.Value, leaseUntil.AddMilliseconds(-1), leaseUntil.AddMilliseconds(1));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropDatabaseAsync(masterConnection, databaseName);
        }
    }

    private static SqlSugarClient CreateSqlServerClient(string connectionString) => new(new ConnectionConfig
    {
        ConnectionString = connectionString,
        DbType = DbType.SqlServer,
        IsAutoCloseConnection = true,
        InitKeyType = InitKeyType.Attribute,
        MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = true },
    });

    private static FaceAttendanceEvent NewEvent(string eventGuid, DateTime now) => new()
    {
        EventGuid = eventGuid,
        ImmutablePayloadHash = new string('a', 64),
        UserGuid = "test-user",
        StoreCode = "TEST",
        DeviceCode = "TEST-IPAD",
        HardwareId = "test-hardware",
        PunchType = "clockIn",
        OccurredAtUtc = now,
        DeviceObservedAtUtc = now,
        LocalSequence = 1,
        RosterVersion = 1,
        EnrollmentVersion = 1,
        TimeAnchorId = "test-anchor",
        TimeTrusted = true,
        PhotoSha256 = new string('b', 64),
        KeyId = "test-key",
        ProtectedPhoto = "protected-photo",
        Signature = "test-signature",
        Status = "queued",
        AttemptCount = 0,
        NextAttemptAtUtc = now,
        ReceivedAtUtc = now,
        UpdatedAtUtc = now,
        RetainUntilUtc = now.AddDays(30),
    };

    private static bool IsLoopbackDataSource(string dataSource)
    {
        var host = dataSource.Trim().Split(',', 2)[0].Trim();
        return host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }

    private static string WithDatabase(string connectionString, string database) => new SqlConnectionStringBuilder(connectionString)
    {
        InitialCatalog = database,
    }.ConnectionString;

    private static async Task CreateDatabaseAsync(string masterConnection, string databaseName) =>
        await ExecuteAsync(masterConnection, $"CREATE DATABASE {QuoteName(databaseName)};");

    private static async Task DropDatabaseAsync(string masterConnection, string databaseName) =>
        await ExecuteAsync(masterConnection, $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE {QuoteName(databaseName)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE {QuoteName(databaseName)};
            END;
            """);

    private static string QuoteName(string databaseName)
    {
        Assert.StartsWith("HBface_schema_test_", databaseName, StringComparison.Ordinal);
        return $"[{databaseName.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static async Task ExecuteAsync(string connectionString, string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync();
        if (value is null || value == DBNull.Value) throw new InvalidOperationException("SQL 标量查询未返回值。");
        return (T)Convert.ChangeType(value, typeof(T));
    }

    private static async Task<List<string>> QueryStringsAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }
}
