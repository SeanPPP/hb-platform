namespace BlazorApp.Api.Services.Background;

/// <summary>商品分店日统计列存索引的行组健康度快照。</summary>
public sealed record ProductStoreDailyColumnstoreHealth(
    long RowGroups,
    long SmallRowGroups,
    long TotalRows,
    long DeletedRows)
{
    public double DeletedRatio => TotalRows > 0 ? (double)DeletedRows / TotalRows : 0d;
}

/// <summary>
/// 决定何时对 IX_LSPSA_Sales_Analytics 执行在线 REORGANIZE。
/// 日统计每天按日删除重写（正常日 30–70 万行、回填日 400–550 万行），列存会累积已删除行和
/// 5000 行一批 BulkCopy 留下的小行组；2026-09-22 生产实测 72% 已删除行、5400 个行组时，
/// 一年聚合从预期的 1–2 秒退化到 27 秒。阈值故意宽松：整理是单线程在线操作，但仍会占一个核心。
/// </summary>
public static class ProductStoreDailyColumnstoreMaintenancePolicy
{
    public const string TableName = "dbo.ProductStoreDailySalesStatistic";
    public const string IndexName = "IX_LSPSA_Sales_Analytics";
    /// <summary>压缩行组的理想大小是 1,048,576 行；不足十分之一的算小行组。</summary>
    public const int SmallRowGroupRows = 102400;
    public const double DeletedRatioThreshold = 0.2;
    public const int SmallRowGroupThreshold = 64;

    /// <summary>用目录视图而非 physical_stats DMV：行组上千时后者要扫描每个段，曾耗时 26 秒。</summary>
    public const string HealthSql = """
SELECT COUNT_BIG(*) AS RowGroups,
       SUM(CASE WHEN rg.total_rows - rg.deleted_rows < 102400 THEN 1 ELSE 0 END) AS SmallRowGroups,
       SUM(CAST(rg.total_rows AS bigint)) AS TotalRows,
       SUM(CAST(rg.deleted_rows AS bigint)) AS DeletedRows
FROM sys.column_store_row_groups rg
INNER JOIN sys.indexes i ON i.object_id = rg.object_id AND i.index_id = rg.index_id
WHERE rg.object_id = OBJECT_ID(N'dbo.ProductStoreDailySalesStatistic')
  AND i.name = N'IX_LSPSA_Sales_Analytics'
  AND rg.state_description IN (N'COMPRESSED', N'OPEN', N'CLOSED');
""";

    public const string IndexExistsSql = """
SELECT COUNT(*) FROM sys.indexes
WHERE object_id = OBJECT_ID(N'dbo.ProductStoreDailySalesStatistic') AND name = N'IX_LSPSA_Sales_Analytics' AND type = 6 AND is_disabled = 0;
""";

    /// <summary>在线整理：合并小行组、物理移除已删除行、压缩增量行组，可随时中断。</summary>
    public const string ReorganizeSql =
        "ALTER INDEX [IX_LSPSA_Sales_Analytics] ON [dbo].[ProductStoreDailySalesStatistic] REORGANIZE WITH (COMPRESS_ALL_ROW_GROUPS = ON);";

    public static bool ShouldReorganize(
        ProductStoreDailyColumnstoreHealth health,
        DateTime? lastReorganizedAtUtc,
        DateTime nowUtc,
        TimeSpan minimumInterval)
    {
        if (lastReorganizedAtUtc.HasValue && nowUtc - lastReorganizedAtUtc.Value < minimumInterval)
            return false;
        if (health.TotalRows <= 0)
            return false;
        return health.DeletedRatio >= DeletedRatioThreshold || health.SmallRowGroups >= SmallRowGroupThreshold;
    }
}
