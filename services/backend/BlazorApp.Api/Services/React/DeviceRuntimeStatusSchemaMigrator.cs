using SqlSugar;

namespace BlazorApp.Api.Services.React;

public static class DeviceRuntimeStatusSchemaMigrator
{
    private const string EnsureDeviceRuntimeStatusColumnsSql = """
        IF OBJECT_ID(N'[dbo].[POSM_设备注册信息表]', N'U') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'dbo.POSM_设备注册信息表', N'是否在线') IS NULL
            BEGIN
                ALTER TABLE [dbo].[POSM_设备注册信息表]
                    ADD [是否在线] BIT NOT NULL
                    CONSTRAINT [DF_POSM_DeviceRegistration_IsOnline] DEFAULT (0) WITH VALUES;
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

    internal static IReadOnlyList<string> SqlScriptsForTests { get; } =
    [
        EnsureDeviceRuntimeStatusColumnsSql,
    ];

    public static async Task EnsureAsync(ISqlSugarClient db, ILogger logger)
    {
        foreach (var sql in SqlScriptsForTests)
        {
            await db.Ado.ExecuteCommandAsync(sql);
        }

        logger.LogInformation("POSM 设备运行状态字段结构检查完成");
    }
}
