using BlazorApp.Shared.Models.HBweb;
using Hbpos.Api.Data;
using Hbpos.Contracts.Promotions;
using SqlSugar;

namespace Hbpos.Api.Services;

public interface IPromotionRuleService
{
    Task<PromotionRulesResponse> GetRulesAsync(
        string storeCode,
        DateTimeOffset? asOf,
        CancellationToken cancellationToken);
}

public sealed class PromotionRuleService(
    HbposSqlSugarContext dbContext,
    TimeProvider? timeProvider = null) : IPromotionRuleService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<PromotionRulesResponse> GetRulesAsync(
        string storeCode,
        DateTimeOffset? asOf,
        CancellationToken cancellationToken)
    {
        var normalizedStoreCode = NormalizeStoreCode(storeCode);
        var generatedAt = _timeProvider.GetUtcNow();
        var effectiveAtUtc = (asOf ?? generatedAt).ToUniversalTime().UtcDateTime;

        // 只返回未软删除的有效促销；门店范围和总部规则判定都只看未软删除的绑定记录。
        var promotions = await dbContext.MainDb.Queryable<Promotion>()
            .Where(promotion => !promotion.IsDeleted)
            .Where(promotion => promotion.IsEnabled)
            .Where(promotion => promotion.EffectiveStart <= effectiveAtUtc)
            .Where(promotion => promotion.EffectiveEnd >= effectiveAtUtc)
            .Where(promotion =>
                SqlFunc
                    .Subqueryable<PromotionStore>()
                    .Where(store =>
                        !store.IsDeleted
                        && store.PromotionId == promotion.Id
                        && store.StoreCode == normalizedStoreCode)
                    .Any()
                || !SqlFunc
                    .Subqueryable<PromotionStore>()
                    .Where(store =>
                        !store.IsDeleted
                        && store.PromotionId == promotion.Id)
                    .Any())
            .OrderBy(promotion => promotion.Priority, OrderByType.Desc)
            .OrderBy(promotion => promotion.EffectiveStart, OrderByType.Asc)
            .OrderBy(promotion => promotion.Id, OrderByType.Asc)
            .ToListAsync(cancellationToken);

        var promotionIds = promotions.Select(item => item.Id).ToList();
        var products = promotionIds.Count == 0
            ? []
            : await dbContext.MainDb.Queryable<PromotionProduct>()
                .Where(product => !product.IsDeleted)
                .Where(product => promotionIds.Contains(product.PromotionId))
                .OrderBy(product => product.PromotionId, OrderByType.Asc)
                .OrderBy(product => product.ProductCode, OrderByType.Asc)
                .ToListAsync(cancellationToken);

        // 一次性装载未软删除的商品明细，避免按促销逐条回表形成 N+1 查询。
        var productsByPromotionId = products
            .GroupBy(item => item.PromotionId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PromotionProduct>)group.ToList());

        var rules = promotions
            .Select(promotion => MapToDto(
                promotion,
                productsByPromotionId.TryGetValue(promotion.Id, out var promotionProducts)
                    ? promotionProducts
                    : []))
            .ToArray();

        return new PromotionRulesResponse(
            normalizedStoreCode,
            generatedAt,
            rules);
    }

    private static PromotionRuleDto MapToDto(
        Promotion promotion,
        IReadOnlyList<PromotionProduct> products)
    {
        return new PromotionRuleDto(
            promotion.Id,
            promotion.Name,
            DateTime.SpecifyKind(promotion.EffectiveStart, DateTimeKind.Utc),
            DateTime.SpecifyKind(promotion.EffectiveEnd, DateTimeKind.Utc),
            promotion.IsExclusive,
            promotion.Priority,
            promotion.ApplyQuantity,
            promotion.FixedPrice,
            promotion.MaxApplicationsPerOrder,
            products.Select(MapProductToDto).ToArray());
    }

    private static PromotionRuleProductDto MapProductToDto(PromotionProduct product)
    {
        return new PromotionRuleProductDto(
            product.ProductCode,
            product.UnitWeight);
    }

    private static string NormalizeStoreCode(string? value)
    {
        return (value ?? string.Empty).Trim();
    }
}
