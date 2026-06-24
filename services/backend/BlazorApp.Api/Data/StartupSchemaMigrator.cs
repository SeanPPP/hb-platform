using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Data
{
    public static class StartupSchemaMigrator
    {
        public static async Task EnsureAsync(ISqlSugarClient db, ILogger logger)
        {
            if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
            {
                return;
            }

            // 关键位置：统一串起所有启动兜底迁移，避免 Program.cs 只接入其中一条补列链路。
            await LocalSupplierInvoiceStartupSchemaMigrator.EnsureAsync(db, logger);
            await EnsureMobileAppBuildCosMirrorColumnsAsync(db, logger);
        }

        private static async Task EnsureMobileAppBuildCosMirrorColumnsAsync(
            ISqlSugarClient db,
            ILogger logger
        )
        {
            const string sql =
                @"
IF OBJECT_ID('MobileAppBuild', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('MobileAppBuild', 'CosArtifactUrl') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosArtifactUrl] nvarchar(1000) NULL;
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosObjectKey') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosObjectKey] nvarchar(500) NULL;
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosMirroredAt') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosMirroredAt] datetime2 NULL;
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosMirrorError') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosMirrorError] nvarchar(1000) NULL;
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosMirrorStatus') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosMirrorStatus] nvarchar(32) NOT NULL
            CONSTRAINT [DF_MobileAppBuild_CosMirrorStatus] DEFAULT('pending');
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosMirrorAttempts') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosMirrorAttempts] int NOT NULL
            CONSTRAINT [DF_MobileAppBuild_CosMirrorAttempts] DEFAULT(0);
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosMirrorLastAttemptAtUtc') IS NULL
    BEGIN
        ALTER TABLE [MobileAppBuild]
        ADD [CosMirrorLastAttemptAtUtc] datetime2 NULL;
    END;

    IF COL_LENGTH('MobileAppBuild', 'CosArtifactUrl') IS NOT NULL
       AND COL_LENGTH('MobileAppBuild', 'CosMirrorStatus') IS NOT NULL
    BEGIN
        EXEC(N'
            UPDATE [MobileAppBuild]
            SET [CosMirrorStatus] = ''succeeded''
            WHERE [CosArtifactUrl] IS NOT NULL
              AND LTRIM(RTRIM([CosArtifactUrl])) <> ''''
              AND ([CosMirrorStatus] IS NULL OR [CosMirrorStatus] = ''pending'');
        ');
    END;
END;";

            // 关键位置：移动端自更新依赖这些状态列判断 COS、EAS 回退和 unsafe 排除，启动时补齐可降低旧库发布风险。
            await db.Ado.ExecuteCommandAsync(sql);
            logger.LogInformation("移动端 APK 构建 COS 镜像字段检查完成");
        }
    }
}
