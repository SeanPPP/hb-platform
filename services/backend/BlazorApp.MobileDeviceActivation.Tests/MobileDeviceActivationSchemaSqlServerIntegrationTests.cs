using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.SqlClient;
using Xunit;

namespace BlazorApp.MobileDeviceActivation.Tests;

public sealed class MobileDeviceActivationSqlServerFactAttribute : FactAttribute
{
    private const string ConnectionEnvironmentVariable =
        "HBWEB_SCHEMA_SQLSERVER_TEST_CONNECTION";

    public MobileDeviceActivationSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
        {
            Skip = $"未配置 {ConnectionEnvironmentVariable}，跳过隔离的 Mobile schema SQL Server 测试。";
        }
    }
}

[CollectionDefinition(
    nameof(MobileDeviceActivationSchemaSqlServerCollection),
    DisableParallelization = true)]
public sealed class MobileDeviceActivationSchemaSqlServerCollection;

[Collection(nameof(MobileDeviceActivationSchemaSqlServerCollection))]
[Trait("Category", "SQL")]
public sealed class MobileDeviceActivationSchemaSqlServerIntegrationTests
{
    [MobileDeviceActivationSqlServerFact]
    public async Task 已迁移库_关键列约束主键或唯一索引漂移_只读签名均失败关闭()
    {
        await using var database = await IsolatedMobileSchemaDatabase.CreateAsync();
        await database.ExecuteAsync(MobileDeviceActivationSchema.EnsureSql);
        await database.ExecuteAsync(MobileDeviceActivationSchema.VerifySql);

        await database.ExecuteAsync(
            "ALTER TABLE [dbo].[POSM_MobileDeviceAccountBinding] ALTER COLUMN [TargetUserGuid] varchar(63) NOT NULL;");
        await AssertSqlFailureAsync(database, expectedError: 51402);
        await database.ExecuteAsync(
            "ALTER TABLE [dbo].[POSM_MobileDeviceAccountBinding] ALTER COLUMN [TargetUserGuid] varchar(64) NOT NULL;");
        await database.ExecuteAsync(MobileDeviceActivationSchema.VerifySql);

        await database.ExecuteAsync(
            """
            ALTER TABLE [dbo].[POSM_MobileDeviceActivationGrant]
                DROP CONSTRAINT [CK_POSM_MobileDeviceActivationGrant_System];
            ALTER TABLE [dbo].[POSM_MobileDeviceActivationGrant] WITH CHECK
                ADD CONSTRAINT [CK_POSM_MobileDeviceActivationGrant_System]
                CHECK ([DeviceSystem] IN ('Android', 'iOS') OR 1 = 1);
            """);
        await AssertSqlFailureAsync(database, expectedError: 51408);
        await database.ExecuteAsync(
            """
            ALTER TABLE [dbo].[POSM_MobileDeviceActivationGrant]
                DROP CONSTRAINT [CK_POSM_MobileDeviceActivationGrant_System];
            ALTER TABLE [dbo].[POSM_MobileDeviceActivationGrant] WITH CHECK
                ADD CONSTRAINT [CK_POSM_MobileDeviceActivationGrant_System]
                CHECK ([DeviceSystem] IN ('Android', 'iOS'));
            """);
        await database.ExecuteAsync(MobileDeviceActivationSchema.VerifySql);

        await database.ExecuteAsync(
            """
            ALTER TABLE [dbo].[POSM_MobileDeviceAccountBinding]
                DROP CONSTRAINT [PK_POSM_MobileDeviceAccountBinding];
            ALTER TABLE [dbo].[POSM_MobileDeviceAccountBinding]
                ADD CONSTRAINT [PK_POSM_MobileDeviceAccountBinding]
                PRIMARY KEY NONCLUSTERED ([BindingId]);
            """);
        await AssertSqlFailureAsync(database, expectedError: 51405);
        await database.ExecuteAsync(
            """
            ALTER TABLE [dbo].[POSM_MobileDeviceAccountBinding]
                DROP CONSTRAINT [PK_POSM_MobileDeviceAccountBinding];
            ALTER TABLE [dbo].[POSM_MobileDeviceAccountBinding]
                ADD CONSTRAINT [PK_POSM_MobileDeviceAccountBinding]
                PRIMARY KEY CLUSTERED ([BindingId]);
            """);
        await database.ExecuteAsync(MobileDeviceActivationSchema.VerifySql);

        await database.ExecuteAsync(
            "ALTER INDEX [UX_POSM_MobileDeviceAccountBinding_ActiveHardware] ON [dbo].[POSM_MobileDeviceAccountBinding] DISABLE;");
        await AssertSqlFailureAsync(database, expectedError: 51410);
        await database.ExecuteAsync(
            "ALTER INDEX [UX_POSM_MobileDeviceAccountBinding_ActiveHardware] ON [dbo].[POSM_MobileDeviceAccountBinding] REBUILD;");
        await database.ExecuteAsync(MobileDeviceActivationSchema.VerifySql);
    }

    private static async Task AssertSqlFailureAsync(
        IsolatedMobileSchemaDatabase database,
        int expectedError)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(
            () => database.ExecuteAsync(MobileDeviceActivationSchema.VerifySql));
        Assert.Equal(expectedError, exception.Number);
    }

    private sealed class IsolatedMobileSchemaDatabase : IAsyncDisposable
    {
        private const string ConnectionEnvironmentVariable =
            "HBWEB_SCHEMA_SQLSERVER_TEST_CONNECTION";
        private readonly string _masterConnectionString;
        private readonly string _databaseName;

        private IsolatedMobileSchemaDatabase(
            string masterConnectionString,
            string databaseConnectionString,
            string databaseName)
        {
            _masterConnectionString = masterConnectionString;
            ConnectionString = databaseConnectionString;
            _databaseName = databaseName;
        }

        public string ConnectionString { get; }

        public static async Task<IsolatedMobileSchemaDatabase> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured))
            {
                throw new InvalidOperationException(
                    $"Missing dedicated SQL Server connection: {ConnectionEnvironmentVariable}.");
            }

            var databaseName = $"HBMobileActivationTest_{Guid.NewGuid():N}";
            var masterBuilder = new SqlConnectionStringBuilder(configured)
            {
                InitialCatalog = "master",
            };
            var databaseBuilder = new SqlConnectionStringBuilder(masterBuilder.ConnectionString)
            {
                InitialCatalog = databaseName,
            };

            await using var connection = new SqlConnection(masterBuilder.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{databaseName}];";
            command.CommandTimeout = 300;
            await command.ExecuteNonQueryAsync();

            return new IsolatedMobileSchemaDatabase(
                masterBuilder.ConnectionString,
                databaseBuilder.ConnectionString,
                databaseName);
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection)
            {
                CommandTimeout = 300,
            };
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(_masterConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{_databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{_databaseName}];
                END;
                """;
            command.CommandTimeout = 300;
            await command.ExecuteNonQueryAsync();
        }
    }
}
