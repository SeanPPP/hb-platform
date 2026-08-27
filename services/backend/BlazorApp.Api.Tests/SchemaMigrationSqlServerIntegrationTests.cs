using BlazorApp.Api.Data;
using BlazorApp.Api.Data.SchemaMigrations;
using BlazorApp.Api.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>
/// 显式迁移的真实 SQL Server 回归测试。
///
/// 只接受专用测试连接，且每个用例均创建 GUID 命名的两个隔离数据库；绝不读取
/// appsettings 或现有 HBWeb 数据库连接。默认环境未配置时在发现阶段跳过。
/// </summary>
public sealed class SchemaMigrationSqlServerFactAttribute : FactAttribute
{
    private const string ConnectionEnvironmentVariable =
        "HBWEB_SCHEMA_SQLSERVER_TEST_CONNECTION";

    public SchemaMigrationSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
        {
            Skip = $"未配置 {ConnectionEnvironmentVariable}，跳过完全隔离的 HBWeb schema SQL Server 集成测试。";
        }
    }
}

[CollectionDefinition(nameof(SchemaMigrationSqlServerCollection), DisableParallelization = true)]
public sealed class SchemaMigrationSqlServerCollection { }

[Collection(nameof(SchemaMigrationSqlServerCollection))]
[Trait("Category", "SQL")]
public sealed class SchemaMigrationSqlServerIntegrationTests
{
    private const string ConnectionEnvironmentVariable =
        "HBWEB_SCHEMA_SQLSERVER_TEST_CONNECTION";

    [SchemaMigrationSqlServerFact]
    public async Task 空隔离库_迁移检查并重复迁移_两个账本均保持正确()
    {
        await using var databases = await IsolatedSchemaDatabases.CreateAsync();
        var coordinator = databases.CreateCoordinator();

        var emptyDatabaseCheck = await coordinator.CheckAsync(CancellationToken.None);
        Assert.False(emptyDatabaseCheck.Success);
        Assert.Equal(SchemaExitCodes.SchemaNotReady, emptyDatabaseCheck.ExitCode);

        var migrated = await coordinator.MigrateAsync(CancellationToken.None);
        Assert.True(migrated.Success);
        Assert.Equal(SchemaExitCodes.Success, migrated.ExitCode);

        var checkedResult = await coordinator.CheckAsync(CancellationToken.None);
        Assert.True(checkedResult.Success);
        Assert.Equal(SchemaExitCodes.Success, checkedResult.ExitCode);

        var repeatedMigration = await coordinator.MigrateAsync(CancellationToken.None);
        Assert.True(repeatedMigration.Success);
        Assert.Equal(SchemaExitCodes.Success, repeatedMigration.ExitCode);

        await AssertHistoryTableAsync(
            databases.MainConnectionString,
            SqlServerSchemaMigrationRuntime.MainHistoryTable,
            SchemaMigrationCoordinator.MainMigrationId
        );
        await AssertHistoryTableAsync(
            databases.PosmConnectionString,
            SqlServerSchemaMigrationRuntime.PosmHistoryTable,
            SchemaMigrationCoordinator.PosmMigrationId
        );
    }

    [SchemaMigrationSqlServerFact]
    public async Task POSM激活表不兼容_主库账本保留且修复后只续跑POSM()
    {
        await using var databases = await IsolatedSchemaDatabases.CreateAsync();
        await ExecuteNonQueryAsync(
            databases.PosmConnectionString,
            "CREATE TABLE [dbo].[POSM_DeviceActivationGrant] ([GrantId] int NOT NULL);"
        );

        var first = await databases.CreateCoordinator().MigrateAsync(CancellationToken.None);

        Assert.False(first.Success);
        Assert.Equal(SchemaExitCodes.DatabaseFailure, first.ExitCode);
        Assert.True(
            await IsMigrationAppliedAsync(
                databases.MainConnectionString,
                SqlServerSchemaMigrationRuntime.MainHistoryTable,
                SchemaMigrationCoordinator.MainMigrationId
            )
        );
        Assert.False(
            await IsMigrationAppliedAsync(
                databases.PosmConnectionString,
                SqlServerSchemaMigrationRuntime.PosmHistoryTable,
                SchemaMigrationCoordinator.PosmMigrationId
            )
        );

        await ExecuteNonQueryAsync(
            databases.PosmConnectionString,
            "DROP TABLE [dbo].[POSM_DeviceActivationGrant];"
        );

        var rerun = await databases.CreateCoordinator().MigrateAsync(CancellationToken.None);
        Assert.True(rerun.Success);
        Assert.Equal(SchemaExitCodes.Success, rerun.ExitCode);
        Assert.True(
            await IsMigrationAppliedAsync(
                databases.PosmConnectionString,
                SqlServerSchemaMigrationRuntime.PosmHistoryTable,
                SchemaMigrationCoordinator.PosmMigrationId
            )
        );
    }

    [SchemaMigrationSqlServerFact]
    public async Task 同库并发迁移锁_第二个Session立即不可用()
    {
        await using var databases = await IsolatedSchemaDatabases.CreateAsync();
        var lockResource = $"HBWeb:SchemaMigration:Integration:{Guid.NewGuid():N}";

        await using var first = await SqlServerSchemaMigrationLock.AcquireAsync(
            databases.MainConnectionString,
            lockResource,
            commandTimeoutSeconds: 30,
            CancellationToken.None
        );

        var exception = await Assert.ThrowsAsync<SchemaMigrationLockUnavailableException>(
            () =>
                SqlServerSchemaMigrationLock.AcquireAsync(
                    databases.MainConnectionString,
                    lockResource,
                    commandTimeoutSeconds: 30,
                    CancellationToken.None
                )
        );

        Assert.True(exception.ResultCode < 0);
    }

    [SchemaMigrationSqlServerFact]
    public async Task 旧迁移器吞掉SQL异常_严格baseline仍失败且不输出原始细节()
    {
        await using var databases = await IsolatedSchemaDatabases.CreateAsync();
        var database = databases.CreateMainContext().Db;
        using var capturedOutput = new StringWriter();
        var previousOut = Console.Out;
        const string SensitiveMarker = "SENSITIVE_SCHEMA_SQL_DETAIL";
        Console.SetOut(capturedOutput);

        try
        {
            await Assert.ThrowsAsync<SchemaBaselineSqlFailureException>(
                () =>
                    SqlServerSchemaMigrationRuntime.RunStrictBaselineAsync(
                        database,
                        () =>
                        {
                            try
                            {
                                database.Ado.ExecuteCommand(
                                    "SELECT 1 FROM [dbo].[__HBWebMissingSchemaObject];"
                                );
                            }
                            catch
                            {
                                // 模拟旧迁移器捕获异常后继续执行的历史行为。
                            }

                            Console.WriteLine(SensitiveMarker);
                            return Task.CompletedTask;
                        },
                        CancellationToken.None
                    )
            );
        }
        finally
        {
            Console.SetOut(previousOut);
        }

        Assert.DoesNotContain(SensitiveMarker, capturedOutput.ToString(), StringComparison.Ordinal);
    }

    [SchemaMigrationSqlServerFact]
    public async Task 已迁移库_激活码索引或CHECK漂移_检查返回20且恢复后就绪()
    {
        await using var databases = await IsolatedSchemaDatabases.CreateAsync();
        var coordinator = databases.CreateCoordinator();
        var migrated = await coordinator.MigrateAsync(CancellationToken.None);
        Assert.True(migrated.Success);

        await ExecuteNonQueryAsync(
            databases.PosmConnectionString,
            "ALTER INDEX [UX_POSM_DeviceActivationGrant_SecretHash] ON [dbo].[POSM_DeviceActivationGrant] DISABLE;"
        );
        var disabledIndex = await coordinator.CheckAsync(CancellationToken.None);
        Assert.False(disabledIndex.Success);
        Assert.Equal(SchemaExitCodes.SchemaNotReady, disabledIndex.ExitCode);

        await ExecuteNonQueryAsync(
            databases.PosmConnectionString,
            "ALTER INDEX [UX_POSM_DeviceActivationGrant_SecretHash] ON [dbo].[POSM_DeviceActivationGrant] REBUILD;"
        );
        Assert.True((await coordinator.CheckAsync(CancellationToken.None)).Success);

        await ExecuteNonQueryAsync(
            databases.PosmConnectionString,
            "ALTER TABLE [dbo].[POSM_DeviceActivationGrant] NOCHECK CONSTRAINT [CK_POSM_DeviceActivationGrant_Expiry];"
        );
        var untrustedCheck = await coordinator.CheckAsync(CancellationToken.None);
        Assert.False(untrustedCheck.Success);
        Assert.Equal(SchemaExitCodes.SchemaNotReady, untrustedCheck.ExitCode);

        await ExecuteNonQueryAsync(
            databases.PosmConnectionString,
            "ALTER TABLE [dbo].[POSM_DeviceActivationGrant] WITH CHECK CHECK CONSTRAINT [CK_POSM_DeviceActivationGrant_Expiry];"
        );
        Assert.True((await coordinator.CheckAsync(CancellationToken.None)).Success);
    }

    [SchemaMigrationSqlServerFact]
    public async Task 已迁移库_仅主键约束改名_只读门禁仍就绪()
    {
        await using var databases = await IsolatedSchemaDatabases.CreateAsync();
        var coordinator = databases.CreateCoordinator();
        Assert.True((await coordinator.MigrateAsync(CancellationToken.None)).Success);

        // 安全签名要求主键列及顺序，不应把部署环境中的约束名称当成业务契约。
        await ExecuteNonQueryAsync(
            databases.PosmConnectionString,
            "EXEC sys.sp_rename N'dbo.PK_POSM_DeviceActivationGrant', N'PK_POSM_DeviceActivationGrant_Renamed', N'OBJECT';"
        );

        var checkedResult = await coordinator.CheckAsync(CancellationToken.None);
        Assert.True(checkedResult.Success);
        Assert.Equal(SchemaExitCodes.Success, checkedResult.ExitCode);
    }

    private static async Task AssertHistoryTableAsync(
        string connectionString,
        string tableName,
        string migrationId
    )
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [MigrationId], [AppliedAtUtc], [ApplicationVersion]
            FROM [dbo].[{tableName}]
            WHERE [MigrationId] = @MigrationId;
            """;
        command.Parameters.AddWithValue("@MigrationId", migrationId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"账本 {tableName} 缺少 migration {migrationId}。");
        Assert.Equal(migrationId, reader.GetString(0));
        Assert.NotEqual(default, reader.GetDateTime(1));
        Assert.InRange(reader.GetString(2).Length, 1, 64);

        await reader.CloseAsync();
        command.Parameters.Clear();
        command.CommandText = $"""
            SELECT [name], TYPE_NAME([system_type_id]), [max_length], [is_nullable]
            FROM sys.columns
            WHERE [object_id] = OBJECT_ID(N'[dbo].[{tableName}]', N'U');
            """;
        await using var shapeReader = await command.ExecuteReaderAsync();
        var columns = new Dictionary<string, (string TypeName, short Length, bool Nullable)>(
            StringComparer.Ordinal
        );
        while (await shapeReader.ReadAsync())
        {
            columns.Add(
                shapeReader.GetString(0),
                (shapeReader.GetString(1), shapeReader.GetInt16(2), shapeReader.GetBoolean(3))
            );
        }

        Assert.Equal(
            new Dictionary<string, (string TypeName, short Length, bool Nullable)>(
                StringComparer.Ordinal
            )
            {
                ["MigrationId"] = ("nvarchar", 320, false),
                ["AppliedAtUtc"] = ("datetime2", 8, false),
                ["ApplicationVersion"] = ("nvarchar", 128, false),
            },
            columns
        );
    }

    private static async Task<bool> IsMigrationAppliedAsync(
        string connectionString,
        string tableName,
        string migrationId
    )
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM [dbo].[{tableName}] WHERE [MigrationId] = @MigrationId
            ) THEN 1 ELSE 0 END AS bit);
            """;
        command.Parameters.AddWithValue("@MigrationId", migrationId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteNonQueryAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 300 };
        await command.ExecuteNonQueryAsync();
    }

    private sealed class IsolatedSchemaDatabases : IAsyncDisposable
    {
        private readonly string _masterConnectionString;
        private readonly string _mainDatabaseName;
        private readonly string _posmDatabaseName;

        private IsolatedSchemaDatabases(
            string masterConnectionString,
            string mainConnectionString,
            string posmConnectionString,
            string mainDatabaseName,
            string posmDatabaseName
        )
        {
            _masterConnectionString = masterConnectionString;
            MainConnectionString = mainConnectionString;
            PosmConnectionString = posmConnectionString;
            _mainDatabaseName = mainDatabaseName;
            _posmDatabaseName = posmDatabaseName;
        }

        public string MainConnectionString { get; }

        public string PosmConnectionString { get; }

        public static async Task<IsolatedSchemaDatabases> CreateAsync()
        {
            var suppliedConnectionString = Environment.GetEnvironmentVariable(
                ConnectionEnvironmentVariable
            );
            Assert.False(string.IsNullOrWhiteSpace(suppliedConnectionString));

            var masterConnectionString = BuildConnectionString(suppliedConnectionString!, "master");
            var suffix = Guid.NewGuid().ToString("N");
            var mainDatabaseName = $"HbWebSchemaMain_{suffix}";
            var posmDatabaseName = $"HbWebSchemaPosm_{suffix}";
            await ExecuteNonQueryAsync(
                masterConnectionString,
                $"CREATE DATABASE {QuoteSqlServerName(mainDatabaseName)};"
            );
            try
            {
                await ExecuteNonQueryAsync(
                    masterConnectionString,
                    $"CREATE DATABASE {QuoteSqlServerName(posmDatabaseName)};"
                );
                return new IsolatedSchemaDatabases(
                    masterConnectionString,
                    BuildConnectionString(suppliedConnectionString, mainDatabaseName),
                    BuildConnectionString(suppliedConnectionString, posmDatabaseName),
                    mainDatabaseName,
                    posmDatabaseName
                );
            }
            catch
            {
                SqlConnection.ClearAllPools();
                await DropDatabaseAsync(masterConnectionString, mainDatabaseName);
                SqlConnection.ClearAllPools();
                throw;
            }
        }

        public SchemaMigrationCoordinator CreateCoordinator()
        {
            var configuration = CreateConfiguration();
            var currentUser = new IsolatedMigrationCurrentUserService();
            return new SchemaMigrationCoordinator(
                configuration,
                new SqlSugarContext(
                    configuration,
                    NullLogger<SqlSugarContext>.Instance,
                    currentUser
                ),
                new POSMSqlSugarContext(
                    configuration,
                    currentUser,
                    NullLogger<POSMSqlSugarContext>.Instance
                ),
                NullLogger<SchemaMigrationCoordinator>.Instance
            );
        }

        public SqlSugarContext CreateMainContext()
        {
            var configuration = CreateConfiguration();
            return new SqlSugarContext(
                configuration,
                NullLogger<SqlSugarContext>.Instance,
                new IsolatedMigrationCurrentUserService()
            );
        }

        private IConfigurationRoot CreateConfiguration() =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = MainConnectionString,
                        ["ConnectionStrings:HBPOSMConnection"] = PosmConnectionString,
                        ["Database:CommandTimeoutSeconds"] = "300",
                        ["Database:InitializeOnStartup"] = "false",
                        ["Database:EnableSqlLogging"] = "false",
                    }
                )
                .Build();

        public async ValueTask DisposeAsync()
        {
            SqlConnection.ClearAllPools();
            try
            {
                await DropDatabaseAsync(_masterConnectionString, _posmDatabaseName);
                await DropDatabaseAsync(_masterConnectionString, _mainDatabaseName);
            }
            finally
            {
                SqlConnection.ClearAllPools();
            }
        }

        private static string BuildConnectionString(string connectionString, string databaseName)
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = databaseName,
            };
            return builder.ConnectionString;
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
                """
            );
        }

        private static string QuoteSqlServerName(string name) =>
            $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private sealed class IsolatedMigrationCurrentUserService : ICurrentUserService
    {
        public string GetCurrentUsername() => "SchemaIntegrationTest";

        public string GetCurrentUserGuid() => string.Empty;
    }
}
