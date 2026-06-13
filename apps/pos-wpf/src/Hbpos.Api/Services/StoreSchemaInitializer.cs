using Hbpos.Api.Data;

namespace Hbpos.Api.Services;

public interface IStoreSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public interface IStoreSchemaSqlExecutor
{
    Task ExecuteAsync(string sql, CancellationToken cancellationToken = default);
}

public sealed class SqlSugarStoreSchemaInitializer(
    IStoreSchemaSqlExecutor sqlExecutor) : IStoreSchemaInitializer
{
    // 共享 Store 实体已包含 ContactEmail；POS API 启动时补齐旧库列，避免默认查询全字段时报“列名无效”。
    internal const string EnsureContactEmailColumnSql = """
        IF OBJECT_ID(N'[dbo].[Store]', N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.Store', N'ContactEmail') IS NULL
        BEGIN
            ALTER TABLE [dbo].[Store]
                ADD [ContactEmail] NVARCHAR(100) NULL;
        END;
        """;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return sqlExecutor.ExecuteAsync(EnsureContactEmailColumnSql, cancellationToken);
    }
}

public sealed class SqlSugarStoreSchemaSqlExecutor(
    HbposSqlSugarContext dbContext) : IStoreSchemaSqlExecutor
{
    public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        return dbContext.MainDb.Ado.ExecuteCommandAsync(sql);
    }
}
