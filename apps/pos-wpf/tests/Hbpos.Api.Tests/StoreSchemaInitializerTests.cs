using Hbpos.Api.Services;

namespace Hbpos.Api.Tests;

public sealed class StoreSchemaInitializerTests
{
    [Fact]
    public async Task InitializeAsync_executes_idempotent_contact_email_column_ddl()
    {
        var executor = new CapturingStoreSchemaSqlExecutor();
        var initializer = new SqlSugarStoreSchemaInitializer(executor);

        await initializer.InitializeAsync();

        var sql = Assert.Single(executor.SqlStatements);
        Assert.Contains("IF OBJECT_ID(N'[dbo].[Store]', N'U') IS NOT NULL", sql);
        Assert.Contains("COL_LENGTH(N'dbo.Store', N'ContactEmail') IS NULL", sql);
        Assert.Contains("ALTER TABLE [dbo].[Store]", sql);
        Assert.Contains("ADD [ContactEmail] NVARCHAR(100) NULL", sql);
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
