using BlazorApp.Api.Data;

namespace BlazorApp.Api.Services;

internal enum HourlySalesBackfillSchemaState
{
    Absent,
    Ready,
    Degraded,
}

/// <summary>识别分时发布表、当前版本指针和统一读取视图是否可以安全使用。</summary>
internal static class HourlySalesBackfillProtection
{
    internal const string ReadViewName = "HourlySalesReadStatistic";

    internal static HourlySalesBackfillSchemaState GetSchemaState(SqlSugarContext context)
    {
        try
        {
            var hasDay = context.Db.DbMaintenance.IsAnyTable("HourlySalesBackfillDay", false);
            var hasBatch = context.Db.DbMaintenance.IsAnyTable("HourlySalesBackfillBatch", false);
            var hasPublished = context.Db.DbMaintenance.IsAnyTable("HourlySalesBackfillPublishedRow", false);
            if (!hasDay && !hasBatch && !hasPublished) return HourlySalesBackfillSchemaState.Absent;
            if (!hasDay || !hasBatch || !hasPublished) return HourlySalesBackfillSchemaState.Degraded;

            return context.Db.CurrentConnectionConfig.DbType switch
            {
                SqlSugar.DbType.SqlServer => SqlServerSchemaReady(context)
                    ? HourlySalesBackfillSchemaState.Ready
                    : HourlySalesBackfillSchemaState.Degraded,
                SqlSugar.DbType.Sqlite => SqliteSchemaReady(context)
                    ? HourlySalesBackfillSchemaState.Ready
                    : HourlySalesBackfillSchemaState.Degraded,
                _ => HourlySalesBackfillSchemaState.Degraded,
            };
        }
        catch
        {
            // 结构部分存在或检查失败时，读取和发布都必须失败关闭，不能混用旧表和发布表。
            return HourlySalesBackfillSchemaState.Degraded;
        }
    }

    internal static bool SchemaReady(SqlSugarContext context) =>
        GetSchemaState(context) == HourlySalesBackfillSchemaState.Ready;

    private static bool SqlServerSchemaReady(SqlSugarContext context)
    {
        var structural = context.Db.Ado.GetInt(
            """
        SELECT CASE WHEN
            EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.HourlySalesBackfillDay')
                  AND name = N'UX_HourlySalesBackfillDay_OneAppliedPerDate'
                  AND is_unique = 1 AND has_filter = 1 AND is_disabled = 0)
            AND EXISTS (SELECT 1 FROM sys.triggers
                WHERE object_id = OBJECT_ID(N'dbo.TR_HourlySalesBackfillPublishedRow_Immutable')
                  AND parent_id = OBJECT_ID(N'dbo.HourlySalesBackfillPublishedRow')
                  AND is_disabled = 0)
            AND OBJECT_ID(N'dbo.HourlySalesReadStatistic', N'V') IS NOT NULL
            AND CHARINDEX(N'HourlySalesBackfillPublishedRow', COALESCE(OBJECT_DEFINITION(
                OBJECT_ID(N'dbo.HourlySalesReadStatistic')), N'')) > 0
            AND CHARINDEX(N'HourlySalesStatistic', COALESCE(OBJECT_DEFINITION(
                OBJECT_ID(N'dbo.HourlySalesReadStatistic')), N'')) > 0
            AND CHARINDEX(N'UNION ALL', UPPER(COALESCE(OBJECT_DEFINITION(
                OBJECT_ID(N'dbo.HourlySalesReadStatistic')), N''))) > 0
            THEN 1 ELSE 0 END
        """) == 1;
        if (!structural) return false;
        var definition = context.Db.Ado.GetString(
            "SELECT COALESCE(OBJECT_DEFINITION(OBJECT_ID(N'dbo.HourlySalesReadStatistic')), N'')");
        // 当前程序只认证同一规则版本；程序升级但视图迁移遗漏时必须失败关闭。
        return definition.Contains("HourlySalesBackfillBatch", StringComparison.OrdinalIgnoreCase)
            && definition.Contains(HourlySalesBackfillRules.Version, StringComparison.Ordinal);
    }

    private static bool SqliteSchemaReady(SqlSugarContext context)
    {
        var structural = context.Db.Ado.GetInt(
            """
        SELECT CASE WHEN
            EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'index'
                AND name = 'UX_HourlySalesBackfillDay_OneAppliedPerDate'
                AND sql LIKE '%WHERE%Applied%')
            AND EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'view'
                AND name = 'HourlySalesReadStatistic'
                AND sql LIKE '%HourlySalesBackfillPublishedRow%'
                AND sql LIKE '%HourlySalesStatistic%')
            THEN 1 ELSE 0 END
        """) == 1;
        if (!structural) return false;
        var definition = context.Db.Ado.GetString(
            "SELECT COALESCE(sql, '') FROM sqlite_master WHERE type = 'view' AND name = 'HourlySalesReadStatistic'");
        return definition.Contains("HourlySalesBackfillBatch", StringComparison.OrdinalIgnoreCase)
            && definition.Contains(HourlySalesBackfillRules.Version, StringComparison.Ordinal);
    }
}
