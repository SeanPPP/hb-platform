using Hbpos.Api.Services;

namespace Hbpos.Api.Tests;

public sealed class StoreReceiptProfileSchemaInitializerTests
{
    [Fact]
    public async Task InitializeAsync_executes_idempotent_return_policy_column_ddl()
    {
        var executor = new CapturingStoreSchemaSqlExecutor();
        var initializer = new SqlSugarStoreSchemaInitializer(executor);

        await initializer.InitializeAsync();

        var sql = Assert.Single(executor.SqlStatements);
        Assert.Contains("IF OBJECT_ID(N'[dbo].[Store]', N'U') IS NOT NULL", sql);
        Assert.Contains("COL_LENGTH(N'dbo.Store', N'ReturnPolicy') IS NULL", sql);
        Assert.Contains("ALTER TABLE [dbo].[Store]", sql);
        Assert.Contains("ADD [ReturnPolicy] NVARCHAR(500) NULL", sql);
    }

    [Fact]
    public async Task InitializeAsync_does_not_backfill_existing_rows()
    {
        var executor = new CapturingStoreSchemaSqlExecutor();
        var initializer = new SqlSugarStoreSchemaInitializer(executor);

        await initializer.InitializeAsync();

        var sql = Assert.Single(executor.SqlStatements);
        Assert.DoesNotContain("UPDATE [dbo].[Store]", sql);
        Assert.DoesNotContain("SET [ReturnPolicy]", sql);
    }

    private sealed class CapturingStoreSchemaSqlExecutor : IStoreSchemaSqlExecutor
    {
        public List<string> SqlStatements { get; } = [];

        public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
        {
            SqlStatements.Add(sql);
            return Task.CompletedTask;
        }
    }
}
