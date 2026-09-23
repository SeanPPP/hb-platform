using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.SupplyNotices;

public interface IWarehouseProductSupplyNoticeService
{
    /// <summary>对已下架的商品登记或修改供货说明；在架、已删除、不存在的商品会被跳过并回报。</summary>
    Task<BatchUpsertWarehouseProductSupplyNoticeResultDto> UpsertForPausedProductsAsync(
        BatchUpsertWarehouseProductSupplyNoticeRequestDto request,
        string actor
    );

    Task<List<WarehouseProductSupplyNoticeDto>> GetOpenNoticesAsync(IReadOnlyCollection<string> productCodes);
}

internal sealed class WarehouseProductSupplyNoticeService(SqlSugarContext context)
    : IWarehouseProductSupplyNoticeService
{
    private const int MaxBatchSize = 2000;
    private readonly ISqlSugarClient _db = context.Db;

    public async Task<BatchUpsertWarehouseProductSupplyNoticeResultDto> UpsertForPausedProductsAsync(
        BatchUpsertWarehouseProductSupplyNoticeRequestDto request,
        string actor
    )
    {
        var result = new BatchUpsertWarehouseProductSupplyNoticeResultDto();
        var codes = NormalizeCodes(request?.ProductCodes);
        if (codes.Count == 0)
        {
            result.Message = "商品编码不能为空";
            return result;
        }
        if (codes.Count > MaxBatchSize)
        {
            result.Message = $"一次最多设置 {MaxBatchSize} 个商品";
            return result;
        }

        var (notice, error) = WarehouseProductSupplyNoticeRules.Normalize(request!.Notice);
        if (error != null)
        {
            result.Message = error;
            return result;
        }
        if (!WarehouseProductSupplyNoticeWriter.IsSchemaReady(_db))
        {
            result.Message = "供货说明功能尚未启用，请先执行数据库迁移";
            return result;
        }

        // 只有当前下架的商品才需要供货说明：在架商品带着“预计恢复时间”会自相矛盾。
        var pausedCodes = await _db.Queryable<WarehouseProduct>()
            .Where(item => codes.Contains(item.ProductCode) && !item.IsDeleted && !item.IsActive)
            .Select(item => item.ProductCode)
            .ToListAsync();
        var pausedSet = new HashSet<string>(pausedCodes, StringComparer.OrdinalIgnoreCase);
        result.SkippedProductCodes = codes.Where(code => !pausedSet.Contains(code)).ToList();

        if (pausedCodes.Count > 0)
        {
            var now = DateTime.UtcNow;
            var transaction = await _db.Ado.UseTranAsync(async () =>
            {
                await WarehouseProductSupplyNoticeWriter.UpsertOpenNoticesAsync(
                    _db,
                    pausedCodes,
                    notice!,
                    actor,
                    source: "WarehouseSupplyNotice",
                    now
                );
            });
            if (!transaction.IsSuccess)
            {
                throw transaction.ErrorException ?? new InvalidOperationException("保存供货说明失败");
            }
        }

        result.SuccessCount = pausedCodes.Count;
        result.Success = pausedCodes.Count > 0;
        result.Message = result.SkippedProductCodes.Count == 0
            ? "供货说明已保存"
            : pausedCodes.Count == 0
                ? "所选商品当前都不是下架状态，无需供货说明"
                : $"已保存 {pausedCodes.Count} 个，跳过 {result.SkippedProductCodes.Count} 个（在架或不存在）";
        return result;
    }

    public async Task<List<WarehouseProductSupplyNoticeDto>> GetOpenNoticesAsync(
        IReadOnlyCollection<string> productCodes
    )
    {
        var codes = NormalizeCodes(productCodes);
        if (codes.Count == 0 || !WarehouseProductSupplyNoticeWriter.IsSchemaReady(_db))
        {
            return new List<WarehouseProductSupplyNoticeDto>();
        }

        var notices = await _db.Queryable<WarehouseProductSupplyNotice>()
            .Where(item => codes.Contains(item.ProductCode) && item.ClosedAtUtc == null)
            .ToListAsync();
        if (notices.Count == 0)
        {
            return new List<WarehouseProductSupplyNoticeDto>();
        }

        var noticeCodes = notices.Select(item => item.ProductCode).ToList();
        var watchCounts = (
            await _db.Queryable<StoreProductSupplyWatch>()
                .Where(item =>
                    noticeCodes.Contains(item.ProductCode)
                    && item.Status == StoreProductSupplyWatchStatuses.Watching
                )
                .GroupBy(item => item.ProductCode)
                .Select(item => new { item.ProductCode, Count = SqlFunc.AggregateCount(item.Id) })
                .ToListAsync()
        ).ToDictionary(item => item.ProductCode, item => item.Count, StringComparer.OrdinalIgnoreCase);

        var today = WarehouseProductSupplyNoticeRules.BusinessToday(DateTime.UtcNow);
        return notices
            .Select(notice => new WarehouseProductSupplyNoticeDto
            {
                ProductCode = notice.ProductCode,
                SupplyPlan = notice.SupplyPlan,
                ExpectedFrom = WarehouseProductSupplyNoticeRules.ToDateOnly(notice.ExpectedFrom),
                ExpectedTo = WarehouseProductSupplyNoticeRules.ToDateOnly(notice.ExpectedTo),
                ExpectedPrecision = notice.ExpectedPrecision,
                // 仓库端保留原日期并标逾期，方便看到“当初承诺的是哪天”后再改。
                IsOverdue = WarehouseProductSupplyNoticeRules.IsOverdue(notice.ExpectedTo, today),
                StoreFacingNote = notice.StoreFacingNote,
                InternalNote = notice.InternalNote,
                UpdatedBy = notice.UpdatedBy,
                UpdatedAtUtc = notice.UpdatedAtUtc,
                CreatedAtUtc = notice.CreatedAtUtc,
                WatchingStoreCount = watchCounts.GetValueOrDefault(notice.ProductCode),
            })
            .ToList();
    }

    private static List<string> NormalizeCodes(IEnumerable<string>? productCodes)
    {
        return (productCodes ?? Enumerable.Empty<string>())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
