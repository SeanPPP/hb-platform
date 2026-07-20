using BlazorApp.Api.Services.React;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PreorderSqlServerFactAttribute : FactAttribute
{
    private const string ConnectionEnvironmentVariable =
        "PREORDER_SQLSERVER_TEST_CONNECTION";

    public PreorderSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
        {
            Skip = $"未配置 {ConnectionEnvironmentVariable}，跳过真实 SQL Server Preorder 锁验证。";
        }
    }
}

public sealed class PreorderSqlServerIntegrationTests
{
    private const string SqlServerTestConnectionEnvVar =
        "PREORDER_SQLSERVER_TEST_CONNECTION";

    [PreorderSqlServerFact]
    public async Task 普通订单StoreGate真实SQLServer锁保留数据库StoreGuid大小写()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerTestConnectionEnvVar
        );
        // 关键位置：自定义 Fact 已在发现阶段处理缺失配置，运行时仍防止环境被意外清空。
        Assert.False(string.IsNullOrWhiteSpace(baseConnectionString));

        var databaseName = $"HbPreorderLock_{Guid.NewGuid():N}";
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
            const string canonicalStoreGuid = "MiXeD-Store";
            // 大小写敏感列确保测试能识别错误使用规范化小写锁键的回归。
            await ExecuteNonQueryAsync(
                databaseConnectionString,
                $"""
CREATE TABLE [Store] (
    [StoreGUID] nvarchar(100) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY
);
INSERT INTO [Store] ([StoreGUID]) VALUES (N'{canonicalStoreGuid}');
"""
            );

            using var db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = databaseConnectionString,
                DbType = DbType.SqlServer,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            });
            var resource = PreorderGateEvaluator.GetStoreLockResourceByStoreGuid(
                canonicalStoreGuid
            );
            var transactionStarted = false;
            try
            {
                await db.Ado.BeginTranAsync();
                transactionStarted = true;
                await PreorderGateEvaluator.AcquireDatabaseLockFailClosedAsync(
                    db,
                    resource,
                    PreorderGateEvaluator.GetCanonicalStoreGuidFromLockResource(resource),
                    "S01",
                    NullLogger.Instance
                );
                await db.Ado.CommitTranAsync();
                transactionStarted = false;
            }
            finally
            {
                if (transactionStarted)
                {
                    await db.Ado.RollbackTranAsync();
                }
            }
        }
        finally
        {
            await DropDatabaseAsync(masterConnectionString, databaseName);
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

    private static string QuoteSqlServerName(string name)
    {
        return $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}
