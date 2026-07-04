using Hbpos.Api.Data;

namespace Hbpos.Api.Services;

public interface IDeviceRuntimeStatusSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public interface IDeviceRuntimeStatusSchemaSqlExecutor
{
    Task ExecuteAsync(string sql, CancellationToken cancellationToken = default);
}

public sealed class SqlSugarDeviceRuntimeStatusSchemaInitializer(
    IDeviceRuntimeStatusSchemaSqlExecutor sqlExecutor) : IDeviceRuntimeStatusSchemaInitializer
{
    // 关键逻辑：本地 POS API 也会直接写 POSM 设备表，启动时补齐运行态列，避免心跳上报时报“列名无效”。
    internal const string EnsureRuntimeStatusColumnsSql = """
        IF OBJECT_ID(N'[dbo].[POSM_设备注册信息表]', N'U') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'dbo.POSM_设备注册信息表', N'是否在线') IS NULL
            BEGIN
                ALTER TABLE [dbo].[POSM_设备注册信息表]
                    ADD [是否在线] BIT NOT NULL DEFAULT (0) WITH VALUES;
            END;

            IF COL_LENGTH(N'dbo.POSM_设备注册信息表', N'最后心跳时间') IS NULL
            BEGIN
                ALTER TABLE [dbo].[POSM_设备注册信息表]
                    ADD [最后心跳时间] DATETIME2(7) NULL;
            END;

            IF COL_LENGTH(N'dbo.POSM_设备注册信息表', N'当前收银员ID') IS NULL
            BEGIN
                ALTER TABLE [dbo].[POSM_设备注册信息表]
                    ADD [当前收银员ID] NVARCHAR(100) NULL;
            END;

            IF COL_LENGTH(N'dbo.POSM_设备注册信息表', N'当前收银员姓名') IS NULL
            BEGIN
                ALTER TABLE [dbo].[POSM_设备注册信息表]
                    ADD [当前收银员姓名] NVARCHAR(100) NULL;
            END;

            IF COL_LENGTH(N'dbo.POSM_设备注册信息表', N'收银员登录时间') IS NULL
            BEGIN
                ALTER TABLE [dbo].[POSM_设备注册信息表]
                    ADD [收银员登录时间] DATETIME2(7) NULL;
            END;
        END;
        """;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return sqlExecutor.ExecuteAsync(EnsureRuntimeStatusColumnsSql, cancellationToken);
    }
}

public sealed class SqlSugarDeviceRuntimeStatusSchemaSqlExecutor(
    HbposSqlSugarContext dbContext) : IDeviceRuntimeStatusSchemaSqlExecutor
{
    public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        return dbContext.PosmDb.Ado.ExecuteCommandAsync(sql);
    }
}
