using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.SupplyNotices;

public interface IStoreProductSupplyService
{
    /// <summary>按条码 → 货号 → 商品编码精确查询“暂停供货”的商品及其供货说明。</summary>
    Task<StoreProductSupplyLookupResultDto> LookupAsync(string? storeCode, string code);

    Task<(bool Success, string Message)> WatchAsync(string storeCode, string productCode, string actor);

    Task<(bool Success, string Message)> UnwatchAsync(string storeCode, string productCode, string actor);

    Task<List<StoreProductSupplyStatusDto>> GetWatchesAsync(string storeCode);

    Task<StoreProductSupplyWatchSummaryDto> GetWatchSummaryAsync(string storeCode);

    /// <summary>确认“已恢复订货”提醒：只关闭商品当前可订的关注，仍在等待的关注不受影响。</summary>
    Task<int> AcknowledgeRestockedAsync(string storeCode, IReadOnlyCollection<string>? productCodes, string actor);
}

/// <summary>
/// 分店端的商品供货状态与关注。刻意只做精确匹配：仓库有约 1.5 万个历史下架商品，
/// 不提供模糊搜索或浏览，避免把下架目录整体暴露到订货页。
/// </summary>
internal sealed class StoreProductSupplyService(SqlSugarContext context) : IStoreProductSupplyService
{
    private const int LookupLimit = 20;
    private readonly ISqlSugarClient _db = context.Db;

    /// <summary>查询用的商品行：订货端展示所需的最小字段 + 两个决定“能否订货”的状态。</summary>
    private sealed class SupplyProductRow
    {
        public string ProductCode { get; set; } = string.Empty;
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public string? ProductName { get; set; }
        public string? ProductImage { get; set; }
        public bool ProductIsActive { get; set; }
        public bool WarehouseIsActive { get; set; }
    }

    public async Task<StoreProductSupplyLookupResultDto> LookupAsync(string? storeCode, string code)
    {
        var trimmed = code.Trim();
        var result = new StoreProductSupplyLookupResultDto { Code = trimmed };
        if (trimmed.Length == 0 || !WarehouseProductSupplyNoticeWriter.IsSchemaReady(_db))
        {
            return result;
        }

        // 与订货扫码同序：条码优先，其次货号，最后商品编码；命中即止。
        foreach (var matchField in new[] { "barcode", "itemNumber", "productCode" })
        {
            var rows = await QueryPausedProductsAsync(trimmed, matchField);
            if (rows.Count == 0)
            {
                continue;
            }

            result.MatchType = matchField;
            result.Items = await BuildStatusesAsync(storeCode, rows);
            return result;
        }

        return result;
    }

    public async Task<(bool Success, string Message)> WatchAsync(
        string storeCode,
        string productCode,
        string actor
    )
    {
        if (!WarehouseProductSupplyNoticeWriter.IsSchemaReady(_db))
        {
            return (false, "关注功能尚未启用");
        }

        var rows = await QueryProductsByCodesAsync(new[] { productCode });
        var row = rows.FirstOrDefault();
        if (row == null)
        {
            return (false, "商品不存在");
        }
        if (IsOrderable(row))
        {
            return (false, "该商品当前可以订货，无需关注");
        }

        var alreadyWatching = await _db.Queryable<StoreProductSupplyWatch>()
            .Where(item =>
                item.StoreCode == storeCode
                && item.ProductCode == row.ProductCode
                && item.Status == StoreProductSupplyWatchStatuses.Watching
            )
            .AnyAsync();
        if (alreadyWatching)
        {
            return (true, "已关注");
        }

        try
        {
            await _db.Insertable(new StoreProductSupplyWatch
            {
                StoreCode = storeCode,
                ProductCode = row.ProductCode,
                Status = StoreProductSupplyWatchStatuses.Watching,
                CreatedBy = actor,
                CreatedAtUtc = DateTime.UtcNow,
            }).ExecuteCommandAsync();
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            // 两端同时点关注：唯一索引兜底，视为成功。
        }

        return (true, "已关注");
    }

    public async Task<(bool Success, string Message)> UnwatchAsync(
        string storeCode,
        string productCode,
        string actor
    )
    {
        if (!WarehouseProductSupplyNoticeWriter.IsSchemaReady(_db))
        {
            return (true, "已取消关注");
        }

        await CloseWatchesAsync(
            storeCode,
            new[] { productCode.Trim() },
            actor,
            StoreProductSupplyWatchCloseReasons.Unwatched
        );
        return (true, "已取消关注");
    }

    public async Task<List<StoreProductSupplyStatusDto>> GetWatchesAsync(string storeCode)
    {
        if (!WarehouseProductSupplyNoticeWriter.IsSchemaReady(_db))
        {
            return new List<StoreProductSupplyStatusDto>();
        }

        var watches = await QueryWatchingAsync(storeCode);
        if (watches.Count == 0)
        {
            return new List<StoreProductSupplyStatusDto>();
        }

        var rows = await QueryProductsByCodesAsync(watches.Select(item => item.ProductCode).ToList());
        var statuses = await BuildStatusesAsync(storeCode, rows);
        var createdAt = watches
            .GroupBy(item => item.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Max(item => item.CreatedAtUtc), StringComparer.OrdinalIgnoreCase);

        // 已恢复订货的排最前（这是分店最需要处理的），其余按关注时间倒序。
        return statuses
            .OrderByDescending(item => item.IsOrderable)
            .ThenByDescending(item => createdAt.GetValueOrDefault(item.ProductCode))
            .ToList();
    }

    public async Task<StoreProductSupplyWatchSummaryDto> GetWatchSummaryAsync(string storeCode)
    {
        var summary = new StoreProductSupplyWatchSummaryDto();
        if (!WarehouseProductSupplyNoticeWriter.IsSchemaReady(_db))
        {
            return summary;
        }

        var watches = await QueryWatchingAsync(storeCode);
        if (watches.Count == 0)
        {
            return summary;
        }

        var rows = await QueryProductsByCodesAsync(watches.Select(item => item.ProductCode).ToList());
        var orderableCodes = rows
            .Where(IsOrderable)
            .Select(item => item.ProductCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        summary.RestockedCount = watches.Count(item => orderableCodes.Contains(item.ProductCode));
        summary.WatchingCount = watches.Count - summary.RestockedCount;
        return summary;
    }

    public async Task<int> AcknowledgeRestockedAsync(
        string storeCode,
        IReadOnlyCollection<string>? productCodes,
        string actor
    )
    {
        if (!WarehouseProductSupplyNoticeWriter.IsSchemaReady(_db))
        {
            return 0;
        }

        var watches = await QueryWatchingAsync(storeCode);
        var requested = productCodes?
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidateCodes = watches
            .Select(item => item.ProductCode)
            .Where(code => requested == null || requested.Count == 0 || requested.Contains(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidateCodes.Count == 0)
        {
            return 0;
        }

        // 只确认真正已恢复的：仍在等待的关注即使被传进来也必须保留，否则分店会错过之后的恢复提醒。
        var rows = await QueryProductsByCodesAsync(candidateCodes);
        var restockedCodes = rows.Where(IsOrderable).Select(item => item.ProductCode).ToList();
        if (restockedCodes.Count == 0)
        {
            return 0;
        }

        return await CloseWatchesAsync(
            storeCode,
            restockedCodes,
            actor,
            StoreProductSupplyWatchCloseReasons.Acknowledged
        );
    }

    /// <summary>可订货 = 商品主档启用（HQ 管）且仓库在供货，与订货选品查询同口径。</summary>
    private static bool IsOrderable(SupplyProductRow row) => row.ProductIsActive && row.WarehouseIsActive;

    private async Task<List<StoreProductSupplyWatch>> QueryWatchingAsync(string storeCode)
    {
        return await _db.Queryable<StoreProductSupplyWatch>()
            .Where(item =>
                item.StoreCode == storeCode
                && item.Status == StoreProductSupplyWatchStatuses.Watching
            )
            .ToListAsync();
    }

    private async Task<int> CloseWatchesAsync(
        string storeCode,
        IReadOnlyCollection<string> productCodes,
        string actor,
        string reason
    )
    {
        var codes = productCodes.ToList();
        var now = DateTime.UtcNow;
        return await _db.Updateable<StoreProductSupplyWatch>()
            .SetColumns(item => new StoreProductSupplyWatch
            {
                Status = StoreProductSupplyWatchStatuses.Closed,
                ClosedAtUtc = now,
                ClosedBy = actor,
                CloseReason = reason,
            })
            .Where(item =>
                item.StoreCode == storeCode
                && codes.Contains(item.ProductCode)
                && item.Status == StoreProductSupplyWatchStatuses.Watching
            )
            .ExecuteCommandAsync();
    }

    private async Task<List<SupplyProductRow>> QueryPausedProductsAsync(string code, string matchField)
    {
        // SQL Server 库是不区分大小写的排序规则，等值比较即可；SQLite（测试）区分大小写，补大小写变体。
        var useCaseInsensitiveCollation = _db.CurrentConnectionConfig.DbType == DbType.SqlServer;
        var lookupCodes = new[] { code, code.ToUpperInvariant(), code.ToLowerInvariant() }
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var query = _db.Queryable<Product>()
            .InnerJoin<WarehouseProduct>(
                (product, warehouseProduct) => product.ProductCode == warehouseProduct.ProductCode
            )
            // 只查“商品主档启用、仓库暂停供货”的商品：主档被 HQ 停用的商品对分店来说就是不存在。
            .Where(
                (product, warehouseProduct) =>
                    product.IsActive
                    && !product.IsDeleted
                    && !warehouseProduct.IsDeleted
                    && !warehouseProduct.IsActive
            );

        query = matchField switch
        {
            "barcode" => query
                .WhereIF(
                    useCaseInsensitiveCollation,
                    (product, warehouseProduct) => product.Barcode != null && product.Barcode == code
                )
                .WhereIF(
                    !useCaseInsensitiveCollation,
                    (product, warehouseProduct) =>
                        product.Barcode != null && lookupCodes.Contains(product.Barcode)
                ),
            "itemNumber" => query
                .WhereIF(
                    useCaseInsensitiveCollation,
                    (product, warehouseProduct) =>
                        product.ItemNumber != null && product.ItemNumber == code
                )
                .WhereIF(
                    !useCaseInsensitiveCollation,
                    (product, warehouseProduct) =>
                        product.ItemNumber != null && lookupCodes.Contains(product.ItemNumber)
                ),
            _ => query
                .WhereIF(
                    useCaseInsensitiveCollation,
                    (product, warehouseProduct) =>
                        product.ProductCode != null && product.ProductCode == code
                )
                .WhereIF(
                    !useCaseInsensitiveCollation,
                    (product, warehouseProduct) =>
                        product.ProductCode != null && lookupCodes.Contains(product.ProductCode)
                ),
        };

        return await query
            .Take(LookupLimit)
            .Select(
                (product, warehouseProduct) =>
                    new SupplyProductRow
                    {
                        ProductCode = product.ProductCode ?? string.Empty,
                        ItemNumber = product.ItemNumber,
                        Barcode = product.Barcode,
                        ProductName = product.ProductName,
                        ProductImage = product.ProductImage,
                        ProductIsActive = product.IsActive,
                        WarehouseIsActive = warehouseProduct.IsActive,
                    }
            )
            .ToListAsync();
    }

    private async Task<List<SupplyProductRow>> QueryProductsByCodesAsync(IReadOnlyCollection<string> productCodes)
    {
        var codes = productCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (codes.Count == 0)
        {
            return new List<SupplyProductRow>();
        }

        return await _db.Queryable<Product>()
            .InnerJoin<WarehouseProduct>(
                (product, warehouseProduct) => product.ProductCode == warehouseProduct.ProductCode
            )
            .Where(
                (product, warehouseProduct) =>
                    product.ProductCode != null
                    && codes.Contains(product.ProductCode)
                    && !product.IsDeleted
                    && !warehouseProduct.IsDeleted
            )
            .Select(
                (product, warehouseProduct) =>
                    new SupplyProductRow
                    {
                        ProductCode = product.ProductCode ?? string.Empty,
                        ItemNumber = product.ItemNumber,
                        Barcode = product.Barcode,
                        ProductName = product.ProductName,
                        ProductImage = product.ProductImage,
                        ProductIsActive = product.IsActive,
                        WarehouseIsActive = warehouseProduct.IsActive,
                    }
            )
            .ToListAsync();
    }

    private async Task<List<StoreProductSupplyStatusDto>> BuildStatusesAsync(
        string? storeCode,
        List<SupplyProductRow> rows
    )
    {
        var distinctRows = rows
            .GroupBy(item => item.ProductCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var codes = distinctRows.Select(item => item.ProductCode).ToList();
        if (codes.Count == 0)
        {
            return new List<StoreProductSupplyStatusDto>();
        }

        var notices = (
            await _db.Queryable<WarehouseProductSupplyNotice>()
                .Where(item => codes.Contains(item.ProductCode) && item.ClosedAtUtc == null)
                .ToListAsync()
        )
            .GroupBy(item => item.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var watchingCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(storeCode))
        {
            var watching = await _db.Queryable<StoreProductSupplyWatch>()
                .Where(item =>
                    item.StoreCode == storeCode
                    && codes.Contains(item.ProductCode)
                    && item.Status == StoreProductSupplyWatchStatuses.Watching
                )
                .Select(item => item.ProductCode)
                .ToListAsync();
            watchingCodes.UnionWith(watching);
        }

        var today = WarehouseProductSupplyNoticeRules.BusinessToday(DateTime.UtcNow);
        return distinctRows
            .Select(row =>
            {
                var orderable = IsOrderable(row);
                // 商品已恢复订货后，残留的说明（无界面路径漏关）不再有意义，一律不展示。
                notices.TryGetValue(row.ProductCode, out var notice);
                if (orderable)
                {
                    notice = null;
                }

                var overdue =
                    notice != null
                    && WarehouseProductSupplyNoticeRules.IsOverdue(notice.ExpectedTo, today);
                return new StoreProductSupplyStatusDto
                {
                    ProductCode = row.ProductCode,
                    ItemNumber = row.ItemNumber,
                    Barcode = row.Barcode,
                    ProductName = row.ProductName,
                    ProductImage = row.ProductImage,
                    IsOrderable = orderable,
                    HasNotice = notice != null,
                    SupplyPlan = notice?.SupplyPlan ?? WarehouseProductSupplyPlans.Undecided,
                    // 逾期后不再把过期日期当承诺展示给分店，只告诉它“新时间待确认”。
                    ExpectedFrom = overdue ? null : WarehouseProductSupplyNoticeRules.ToDateOnly(notice?.ExpectedFrom),
                    ExpectedTo = overdue ? null : WarehouseProductSupplyNoticeRules.ToDateOnly(notice?.ExpectedTo),
                    ExpectedPrecision = overdue || notice == null
                        ? WarehouseProductSupplyExpectedPrecisions.Unknown
                        : notice.ExpectedPrecision,
                    IsOverdue = overdue,
                    StoreFacingNote = notice?.StoreFacingNote,
                    NoticeUpdatedAtUtc = notice?.UpdatedAtUtc,
                    IsWatching = watchingCodes.Contains(row.ProductCode),
                };
            })
            .ToList();
    }

    private static bool IsUniqueViolation(Exception ex)
    {
        var message = ex.ToString();
        return message.Contains("UX_StoreProductSupplyWatch_Watching_Store_Product", StringComparison.OrdinalIgnoreCase)
            || message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }
}
