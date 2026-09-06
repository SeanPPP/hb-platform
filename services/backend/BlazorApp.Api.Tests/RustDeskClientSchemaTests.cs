using BlazorApp.Api.Data.SchemaMigrations;
using Microsoft.Data.SqlClient;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class RustDeskClientSchemaTests
{
    [Theory]
    [InlineData("--schema=rustdesk-client", "RustDeskClient")]
    [InlineData("--schema=rustdesk-client-check", "RustDeskClientCheck")]
    public void DedicatedCommandsParseWithoutStartingServer(string argument, string expected)
    {
        Assert.Equal(expected, SchemaCommand.Parse([argument]).Mode.ToString());
        Assert.Equal(SchemaCommandMode.Invalid, SchemaCommand.Parse([argument, "--schema=migrate"]).Mode);
        Assert.Equal(SchemaCommandMode.Invalid, SchemaCommand.Parse([argument.ToUpperInvariant()]).Mode);
    }

    [SchemaMigrationSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task SqlServerMigrationIsIdempotentAndRejectsMissingColumns()
    {
        // 仅使用显式测试连接，在 tempdb 内用随机对象名与外层事务保证回滚。
        var config = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HBWEB_SCHEMA_SQLSERVER_TEST_CONNECTION"))
        { InitialCatalog = "tempdb" };
        await using var connection = new SqlConnection(config.ConnectionString);
        await connection.OpenAsync();
        var suffix = Guid.NewGuid().ToString("N");
        string Isolate(string sql) => sql.Replace("HBweb_RustDeskClientSession", $"HBtest_RdSession_{suffix}")
            .Replace("HBweb_RustDeskManagedDevice", $"HBtest_RdDevice_{suffix}");
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        async Task Execute(string sql)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }
        try
        {
            await Execute(Isolate(RustDeskClientSchemaMigrator.ApplySql));
            await Execute(Isolate(RustDeskClientSchemaMigrator.VerifySql));
            await Execute(Isolate(RustDeskClientSchemaMigrator.ApplySql));
            await Execute(Isolate(RustDeskClientSchemaMigrator.VerifySql));
            await Execute($"ALTER TABLE dbo.HBtest_RdSession_{suffix} DROP COLUMN PasswordFingerprint;");
            var exception = await Assert.ThrowsAsync<SqlException>(() => Execute(Isolate(RustDeskClientSchemaMigrator.VerifySql)));
            Assert.Equal(51720, exception.Number);
        }
        finally
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync();
        }
    }
}
