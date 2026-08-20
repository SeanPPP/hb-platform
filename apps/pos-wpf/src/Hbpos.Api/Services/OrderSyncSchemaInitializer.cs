using Hbpos.Api.Data;

namespace Hbpos.Api.Services;

public interface IOrderSyncSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public interface IOrderSyncSchemaSqlExecutor
{
    Task ExecuteAsync(string sql, CancellationToken cancellationToken = default);
}

public sealed class SqlSugarOrderSyncSchemaInitializer(
    IOrderSyncSchemaSqlExecutor sqlExecutor) : IOrderSyncSchemaInitializer
{
    // Square/Linkly 退款引用会同时保存退款号与原交易号，长度可能超过旧库的 100 字符限制。
    internal const string EnsurePaymentReferenceLengthSql = """
        IF OBJECT_ID(N'[dbo].[payment_detail]', N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.payment_detail', N'Reference') IS NOT NULL
           AND COL_LENGTH(N'dbo.payment_detail', N'Reference') < 1000
        BEGIN
            ALTER TABLE [dbo].[payment_detail]
                ALTER COLUMN [Reference] VARCHAR(1000) NULL;
        END;
        """;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return sqlExecutor.ExecuteAsync(EnsurePaymentReferenceLengthSql, cancellationToken);
    }
}

public sealed class SqlSugarOrderSyncSchemaSqlExecutor(
    HbposSqlSugarContext dbContext) : IOrderSyncSchemaSqlExecutor
{
    public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        return dbContext.PosmDb.Ado.ExecuteCommandAsync(sql);
    }
}
