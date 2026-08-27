using System.Data;
using Microsoft.Data.SqlClient;

namespace BlazorApp.Api.Data.SchemaMigrations;

internal sealed class SchemaMigrationLockUnavailableException(int resultCode)
    : Exception("无法取得数据库迁移锁。")
{
    public int ResultCode { get; } = resultCode;
}

internal sealed class SqlServerSchemaMigrationLock : IAsyncDisposable
{
    private const string AcquireSql = """
        DECLARE @Result int;
        EXEC @Result = sys.sp_getapplock
            @Resource = @Resource,
            @LockMode = N'Exclusive',
            @LockOwner = N'Session',
            @LockTimeout = 0;
        SELECT @Result;
        """;

    private const string ReleaseSql = """
        DECLARE @Result int;
        EXEC @Result = sys.sp_releaseapplock
            @Resource = @Resource,
            @LockOwner = N'Session';
        SELECT @Result;
        """;

    private readonly string _resource;
    private readonly SqlConnection _connection;
    private bool _lockHeld;

    private SqlServerSchemaMigrationLock(string resource, SqlConnection connection)
    {
        _resource = resource;
        _connection = connection;
        _lockHeld = true;
    }

    internal SqlConnection Connection => _connection;

    public static async Task<SqlServerSchemaMigrationLock> AcquireAsync(
        string connectionString,
        string resource,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken
    )
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = CreateCommand(
                connection,
                AcquireSql,
                resource,
                commandTimeoutSeconds
            );
            var resultValue = await command.ExecuteScalarAsync(cancellationToken);
            if (resultValue is null or DBNull)
            {
                throw new SchemaMigrationLockUnavailableException(-999);
            }

            var result = Convert.ToInt32(
                resultValue,
                System.Globalization.CultureInfo.InvariantCulture
            );

            if (!IsSuccessfulLockResult(result))
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new SchemaMigrationLockUnavailableException(result);
            }

            return new SqlServerSchemaMigrationLock(resource, connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_lockHeld && _connection.State == ConnectionState.Open)
            {
                await using var command = CreateCommand(
                    _connection,
                    ReleaseSql,
                    _resource,
                    commandTimeoutSeconds: 5
                );
                var releaseValue = await command.ExecuteScalarAsync(CancellationToken.None);
                var releaseResult = releaseValue is null or DBNull
                    ? -999
                    : Convert.ToInt32(
                        releaseValue,
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                if (IsSuccessfulLockResult(releaseResult))
                {
                    _lockHeld = false;
                }
                else
                {
                    // 负返回码表示 SQL Server 未确认释放，禁止该物理会话回到连接池。
                    SqlConnection.ClearPool(_connection);
                }
            }
        }
        catch
        {
            // 释放失败时清除连接池，确保持有 session-owned 锁的物理连接不会被复用。
            SqlConnection.ClearPool(_connection);
        }
        finally
        {
            await _connection.DisposeAsync();
        }
    }

    internal static bool IsSuccessfulLockResult(int resultCode) => resultCode >= 0;

    private static SqlCommand CreateCommand(
        SqlConnection connection,
        string sql,
        string resource,
        int commandTimeoutSeconds
    )
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = commandTimeoutSeconds;
        command.Parameters.Add("@Resource", SqlDbType.NVarChar, 255).Value = resource;
        return command;
    }
}

internal static class SqlServerSchemaMigrationStore
{
    private const string CreateHistoryTableSqlTemplate = """
        IF OBJECT_ID(N'[dbo].[{0}]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[{0}] (
                [MigrationId] nvarchar(160) NOT NULL,
                [AppliedAtUtc] datetime2(7) NOT NULL,
                [ApplicationVersion] nvarchar(64) NOT NULL,
                CONSTRAINT [{1}] PRIMARY KEY ([MigrationId])
            );
        END;
        """;

    private const string ReadHistorySqlTemplate = """
        SET NOCOUNT ON;
        IF OBJECT_ID(N'[dbo].[{0}]', N'U') IS NULL
        BEGIN
            SELECT CAST(0 AS bit);
        END
        ELSE
        BEGIN
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1
                FROM [dbo].[{0}]
                WHERE [MigrationId] = @MigrationId
            ) THEN 1 ELSE 0 END AS bit);
        END;
        """;

    private const string InsertHistorySqlTemplate = """
        INSERT INTO [dbo].[{0}] ([MigrationId], [AppliedAtUtc], [ApplicationVersion])
        VALUES (@MigrationId, SYSUTCDATETIME(), @ApplicationVersion);
        """;

    public static async Task EnsureHistoryTableAsync(
        SqlConnection connection,
        string tableName,
        string primaryKeyName,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            CreateHistoryTableSqlTemplate,
            tableName,
            primaryKeyName
        );
        command.CommandTimeout = commandTimeoutSeconds;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<bool> IsAppliedAsync(
        string connectionString,
        string tableName,
        string migrationId,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await IsAppliedAsync(
            connection,
            tableName,
            migrationId,
            commandTimeoutSeconds,
            cancellationToken
        );
    }

    public static async Task<bool> IsAppliedAsync(
        SqlConnection connection,
        string tableName,
        string migrationId,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            ReadHistorySqlTemplate,
            tableName
        );
        command.CommandTimeout = commandTimeoutSeconds;
        command.Parameters.Add("@MigrationId", SqlDbType.NVarChar, 160).Value = migrationId;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null && result is not DBNull && Convert.ToBoolean(result);
    }

    public static async Task RecordAppliedAsync(
        SqlConnection connection,
        string tableName,
        string migrationId,
        string applicationVersion,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            InsertHistorySqlTemplate,
            tableName
        );
        command.CommandTimeout = commandTimeoutSeconds;
        command.Parameters.Add("@MigrationId", SqlDbType.NVarChar, 160).Value = migrationId;
        command.Parameters.Add("@ApplicationVersion", SqlDbType.NVarChar, 64).Value =
            applicationVersion;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task ExecuteReadOnlyBatchAsync(
        string connectionString,
        string sql,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = commandTimeoutSeconds;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
