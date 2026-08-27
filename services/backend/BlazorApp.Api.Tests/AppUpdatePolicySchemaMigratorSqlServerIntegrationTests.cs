using BlazorApp.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class AppUpdatePolicySqlServerFactAttribute : FactAttribute
{
    private const string ConnectionEnvironmentVariable =
        "PREORDER_SQLSERVER_TEST_CONNECTION";

    public AppUpdatePolicySqlServerFactAttribute()
    {
        if (
            string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)
            )
        )
        {
            Skip = $"未配置 {ConnectionEnvironmentVariable}，跳过真实 SQL Server 更新策略迁移验证。";
        }
    }
}

[Trait("Category", "SQL")]
public sealed class AppUpdatePolicySchemaMigratorSqlServerIntegrationTests
{
    private const string ConnectionEnvironmentVariable =
        "PREORDER_SQLSERVER_TEST_CONNECTION";

    [AppUpdatePolicySqlServerFact]
    public async Task 空库首次执行_创建Mobile最低支持Build列()
    {
        await WithIsolatedDatabaseAsync(async connectionString =>
        {
            using var db = CreateClient(connectionString);

            await AppUpdatePolicySchemaMigrator.EnsureAsync(db, NullLogger.Instance);

            Assert.Equal(1, await CountMobileBuildColumnsAsync(db));
        });
    }

    [AppUpdatePolicySqlServerFact]
    public async Task 空库重复执行_最低支持Build列仍只有一个()
    {
        await WithIsolatedDatabaseAsync(async connectionString =>
        {
            using var db = CreateClient(connectionString);

            await AppUpdatePolicySchemaMigrator.EnsureAsync(db, NullLogger.Instance);
            await AppUpdatePolicySchemaMigrator.EnsureAsync(db, NullLogger.Instance);

            Assert.Equal(1, await CountMobileBuildColumnsAsync(db));
        });
    }

    [AppUpdatePolicySqlServerFact]
    public async Task 旧表已有策略行_执行两次仅追加Nullable列且不改写行()
    {
        await WithIsolatedDatabaseAsync(async connectionString =>
        {
            using var db = CreateClient(connectionString);
            var policyId = Guid.NewGuid();
            var releaseId = Guid.NewGuid();
            var createdAt = new DateTime(2026, 8, 1, 2, 3, 4, DateTimeKind.Utc);
            var updatedAt = new DateTime(2026, 8, 2, 3, 4, 5, DateTimeKind.Utc);
            await db.Ado.ExecuteCommandAsync(
                """
CREATE TABLE [dbo].[MobileIosNativeUpdatePolicy] (
    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_MobileIosNativeUpdatePolicy] PRIMARY KEY,
    [PolicyKey] nvarchar(40) NOT NULL,
    [ReleaseId] uniqueidentifier NULL,
    [MinimumSupportedVersion] nvarchar(64) NULL,
    [ReleaseMessage] nvarchar(1000) NULL,
    [Enabled] bit NOT NULL,
    [PolicyVersion] bigint NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [UpdatedAt] datetime2 NULL,
    [UpdatedBy] nvarchar(max) NULL,
    [IsDeleted] bit NOT NULL CONSTRAINT [DF_MobileIosNativeUpdatePolicy_IsDeleted] DEFAULT(0)
);
INSERT INTO [dbo].[MobileIosNativeUpdatePolicy]
    ([Id], [PolicyKey], [ReleaseId], [MinimumSupportedVersion], [ReleaseMessage],
     [Enabled], [PolicyVersion], [CreatedAt], [CreatedBy], [UpdatedAt], [UpdatedBy], [IsDeleted])
VALUES
    (@Id, N'mobile-ios', @ReleaseId, N'1.0.1', N'保留原策略',
     1, 7, @CreatedAt, N'legacy-admin', @UpdatedAt, N'legacy-publisher', 0);
""",
                new SugarParameter("@Id", policyId),
                new SugarParameter("@ReleaseId", releaseId),
                new SugarParameter("@CreatedAt", createdAt),
                new SugarParameter("@UpdatedAt", updatedAt)
            );

            await AppUpdatePolicySchemaMigrator.EnsureAsync(db, NullLogger.Instance);
            await AppUpdatePolicySchemaMigrator.EnsureAsync(db, NullLogger.Instance);

            Assert.Equal(1, await CountMobileBuildColumnsAsync(db));
            var row = await db.Ado.SqlQuerySingleAsync<LegacyPolicySnapshot>(
                """
SELECT [Id], [PolicyKey], [ReleaseId], [MinimumSupportedVersion],
       [MinimumSupportedBuildNumber], [ReleaseMessage], [Enabled], [PolicyVersion],
       [CreatedAt], [CreatedBy], [UpdatedAt], [UpdatedBy], [IsDeleted]
FROM [dbo].[MobileIosNativeUpdatePolicy]
WHERE [Id] = @Id;
""",
                new SugarParameter("@Id", policyId)
            );

            Assert.NotNull(row);
            Assert.Equal(policyId, row.Id);
            Assert.Equal("mobile-ios", row.PolicyKey);
            Assert.Equal(releaseId, row.ReleaseId);
            Assert.Equal("1.0.1", row.MinimumSupportedVersion);
            Assert.Null(row.MinimumSupportedBuildNumber);
            Assert.Equal("保留原策略", row.ReleaseMessage);
            Assert.True(row.Enabled);
            Assert.Equal(7, row.PolicyVersion);
            Assert.Equal(createdAt, row.CreatedAt);
            Assert.Equal("legacy-admin", row.CreatedBy);
            Assert.Equal(updatedAt, row.UpdatedAt);
            Assert.Equal("legacy-publisher", row.UpdatedBy);
            Assert.False(row.IsDeleted);
        });
    }

    private static async Task<int> CountMobileBuildColumnsAsync(ISqlSugarClient db) =>
        await db.Ado.SqlQuerySingleAsync<int>(
            """
SELECT COUNT(*)
FROM sys.columns
WHERE [object_id] = OBJECT_ID(N'[dbo].[MobileIosNativeUpdatePolicy]')
  AND [name] = N'MinimumSupportedBuildNumber';
"""
        );

    private static async Task WithIsolatedDatabaseAsync(Func<string, Task> action)
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            ConnectionEnvironmentVariable
        );
        Assert.False(string.IsNullOrWhiteSpace(baseConnectionString));

        var databaseName = $"HbAppUpdatePolicy_{Guid.NewGuid():N}";
        var masterConnectionString = BuildConnectionString(baseConnectionString!, "master");
        var databaseConnectionString = BuildConnectionString(
            baseConnectionString!,
            databaseName
        );
        await ExecuteNonQueryAsync(
            masterConnectionString,
            $"CREATE DATABASE {QuoteSqlServerName(databaseName)};"
        );

        try
        {
            await action(databaseConnectionString);
        }
        finally
        {
            await DropDatabaseAsync(masterConnectionString, databaseName);
        }
    }

    private static SqlSugarClient CreateClient(string connectionString) =>
        new(
            new ConnectionConfig
            {
                ConnectionString = connectionString,
                DbType = DbType.SqlServer,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            }
        );

    private static string BuildConnectionString(
        string connectionString,
        string databaseName
    )
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = databaseName,
        };
        return builder.ConnectionString;
    }

    private static async Task ExecuteNonQueryAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 60,
        };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(
        string masterConnectionString,
        string databaseName
    )
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

    private sealed class LegacyPolicySnapshot
    {
        public Guid Id { get; init; }
        public string PolicyKey { get; init; } = string.Empty;
        public Guid? ReleaseId { get; init; }
        public string? MinimumSupportedVersion { get; init; }
        public int? MinimumSupportedBuildNumber { get; init; }
        public string? ReleaseMessage { get; init; }
        public bool Enabled { get; init; }
        public long PolicyVersion { get; init; }
        public DateTime CreatedAt { get; init; }
        public string? CreatedBy { get; init; }
        public DateTime? UpdatedAt { get; init; }
        public string? UpdatedBy { get; init; }
        public bool IsDeleted { get; init; }
    }
}
