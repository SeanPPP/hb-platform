using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.SupplyNotices;

/// <summary>
/// 供货说明的写入收口。无状态：调用方把自己事务里的 db 传进来，说明的写入与上下架同生共死。
/// 任何环境缺表（尚未执行 schema 迁移的生产库、只建了部分表的测试库）时整体降级为 no-op，
/// 绝不能因为这个附加信息打断上下架本身。
/// </summary>
internal static class WarehouseProductSupplyNoticeWriter
{
    private const string NoticeTable = "WarehouseProductSupplyNotice";

    // 只缓存 SQL Server 的“已就绪”：测试用的 SQLite 内存库会用同一连接串反复新建，缓存会串库。
    private static volatile bool _sqlServerSchemaReady;

    /// <summary>
    /// 下架：为这些商品登记说明；已有未关闭说明则就地更新（修改计划或时间不产生新记录）。
    /// 返回实际写入的商品数。
    /// </summary>
    internal static async Task<int> UpsertOpenNoticesAsync(
        ISqlSugarClient db,
        IReadOnlyCollection<string> productCodes,
        NormalizedSupplyNotice notice,
        string actor,
        string source,
        DateTime nowUtc
    )
    {
        var codes = NormalizeCodes(productCodes);
        if (codes.Count == 0 || !IsSchemaReady(db))
        {
            return 0;
        }

        var existingCodes = await db.Queryable<WarehouseProductSupplyNotice>()
            .Where(item => codes.Contains(item.ProductCode) && item.ClosedAtUtc == null)
            .Select(item => item.ProductCode)
            .ToListAsync();
        var existing = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);

        if (existing.Count > 0)
        {
            var toUpdate = existing.ToList();
            await db.Updateable<WarehouseProductSupplyNotice>()
                .SetColumns(item => new WarehouseProductSupplyNotice
                {
                    SupplyPlan = notice.SupplyPlan,
                    ExpectedFrom = notice.ExpectedFrom,
                    ExpectedTo = notice.ExpectedTo,
                    ExpectedPrecision = notice.ExpectedPrecision,
                    StoreFacingNote = notice.StoreFacingNote,
                    InternalNote = notice.InternalNote,
                    UpdatedBy = actor,
                    UpdatedAtUtc = nowUtc,
                })
                .Where(item => toUpdate.Contains(item.ProductCode) && item.ClosedAtUtc == null)
                .ExecuteCommandAsync();
        }

        var toInsert = codes
            .Where(code => !existing.Contains(code))
            .Select(code => new WarehouseProductSupplyNotice
            {
                ProductCode = code,
                SupplyPlan = notice.SupplyPlan,
                ExpectedFrom = notice.ExpectedFrom,
                ExpectedTo = notice.ExpectedTo,
                ExpectedPrecision = notice.ExpectedPrecision,
                StoreFacingNote = notice.StoreFacingNote,
                InternalNote = notice.InternalNote,
                Source = source,
                CreatedBy = actor,
                CreatedAtUtc = nowUtc,
                UpdatedBy = actor,
                UpdatedAtUtc = nowUtc,
            })
            .ToList();
        if (toInsert.Count > 0)
        {
            await db.Insertable(toInsert).ExecuteCommandAsync();
        }

        return codes.Count;
    }

    /// <summary>
    /// 上架收口：关闭这些商品里“当前已在架”的未关闭说明。
    /// 按商品当前状态判断而不是按调用方声称的动作判断，因此幂等，
    /// 任何把商品重新上架的入口（含货柜回写、供应商同步等不带界面的路径）都可以在更新后直接调用。
    /// </summary>
    internal static async Task<int> CloseNoticesForActiveProductsAsync(
        ISqlSugarClient db,
        IReadOnlyCollection<string> productCodes,
        string actor,
        DateTime nowUtc
    )
    {
        var codes = NormalizeCodes(productCodes);
        if (codes.Count == 0 || !IsSchemaReady(db))
        {
            return 0;
        }

        var openCodes = await db.Queryable<WarehouseProductSupplyNotice>()
            .Where(item => codes.Contains(item.ProductCode) && item.ClosedAtUtc == null)
            .Select(item => item.ProductCode)
            .ToListAsync();
        if (openCodes.Count == 0)
        {
            return 0;
        }

        var activeCodes = await db.Queryable<WarehouseProduct>()
            .Where(item => openCodes.Contains(item.ProductCode) && !item.IsDeleted && item.IsActive)
            .Select(item => item.ProductCode)
            .ToListAsync();
        if (activeCodes.Count == 0)
        {
            return 0;
        }

        return await db.Updateable<WarehouseProductSupplyNotice>()
            .SetColumns(item => new WarehouseProductSupplyNotice
            {
                ClosedAtUtc = nowUtc,
                ClosedBy = actor,
            })
            .Where(item => activeCodes.Contains(item.ProductCode) && item.ClosedAtUtc == null)
            .ExecuteCommandAsync();
    }

    /// <summary>
    /// 上下架入口的统一挂钩：上架则关闭说明；下架且带了说明则登记，没带说明则保持原状
    /// （旧客户端与无界面入口不会带说明，分店端按“后续计划待确认”展示）。
    /// </summary>
    internal static async Task ApplyStatusChangeAsync(
        ISqlSugarClient db,
        IReadOnlyCollection<string> productCodes,
        bool isActive,
        NormalizedSupplyNotice? notice,
        string actor,
        string source,
        DateTime nowUtc
    )
    {
        if (isActive)
        {
            await CloseNoticesForActiveProductsAsync(db, productCodes, actor, nowUtc);
            return;
        }

        if (notice != null)
        {
            await UpsertOpenNoticesAsync(db, productCodes, notice, actor, source, nowUtc);
        }
    }

    internal static bool IsSchemaReady(ISqlSugarClient db)
    {
        var isSqlServer = db.CurrentConnectionConfig.DbType == DbType.SqlServer;
        if (isSqlServer && _sqlServerSchemaReady)
        {
            return true;
        }

        try
        {
            var ready =
                db.DbMaintenance.IsAnyTable(NoticeTable, false)
                && db.DbMaintenance.IsAnyTable("StoreProductSupplyWatch", false);
            if (ready && isSqlServer)
            {
                _sqlServerSchemaReady = true;
            }
            return ready;
        }
        catch
        {
            return false;
        }
    }

    private static List<string> NormalizeCodes(IEnumerable<string> productCodes)
    {
        return productCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
