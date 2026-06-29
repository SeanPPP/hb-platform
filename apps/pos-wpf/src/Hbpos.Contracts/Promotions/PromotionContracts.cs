namespace Hbpos.Contracts.Promotions;

public sealed record PromotionRuleDto(
    string Id,
    string Name,
    DateTime EffectiveStart,
    DateTime EffectiveEnd,
    bool IsExclusive,
    int Priority,
    int ApplyQuantity,
    decimal FixedPrice,
    int? MaxApplicationsPerOrder,
    IReadOnlyList<PromotionRuleProductDto> Products);

public sealed record PromotionRuleProductDto(
    string ProductCode,
    int UnitWeight);

public sealed record PromotionRulesResponse(
    string StoreCode,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<PromotionRuleDto> Rules);
