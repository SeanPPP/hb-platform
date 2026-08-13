namespace Hbpos.Client.Wpf.Models;

/// <summary>
/// 共享挂单冻结 canonical，与 iPad PricingCartStateSnapshot 的 wire 形状逐字段对齐：
/// version=1，pricingState 含 revision/mode/asOfIso/promotions/lines，金额一律整数 cents。
/// 字段名/类型与 Hbpos.Contracts.HeldOrders.SharedSaleCartV1 冻结契约精确一致：
/// promotion 使用 fixedPriceCents 标量（无 Money 对象）、discountState 使用 mode
/// （无 kind）、line quantity 为 decimal、unitPriceCents/fixedPriceCents 为 long。
/// </summary>
public sealed record SharedHeldOrderCanonicalPayload(
    int Version,
    SharedHeldOrderPricingState PricingState)
{
    public const int VersionV1 = 1;
    public const int VersionV2 = 2;

    // 兼容既有调用方/fixture：未显式选择版本时仍构造冻结 V1。
    public const int CurrentVersion = 1;
}

public sealed record SharedHeldOrderPricingState(
    int Revision,
    string Mode,
    string AsOfIso,
    IReadOnlyList<SharedHeldOrderPromotionDefinition> Promotions,
    IReadOnlyList<SharedHeldOrderPricingLine> Lines);

public sealed record SharedHeldOrderPromotionDefinition(
    string Id,
    string Name,
    string EffectiveStartIso,
    string EffectiveEndIso,
    bool IsExclusive,
    int Priority,
    int ApplyQuantity,
    long FixedPriceCents,
    int? MaxApplicationsPerOrder,
    IReadOnlyList<SharedHeldOrderPromotionProduct> Products);

public sealed record SharedHeldOrderPromotionProduct(string ProductCode, decimal UnitWeight);

public sealed record SharedHeldOrderPricingLine(
    string LineId,
    string ProductCode,
    string? ItemNumber,
    string LookupCode,
    string DisplayName,
    decimal Quantity,
    long UnitPriceCents,
    string BasePriceSource,
    SharedHeldOrderLineSyncProvenance? SyncProvenance,
    string Kind,
    string? ReturnSourceKey,
    Guid? OriginalOrderGuid,
    Guid? OriginalOrderDetailGuid,
    SharedHeldOrderDiscountState DiscountState,
    int CatalogDiscountBasisPoints = 0);

public sealed record SharedHeldOrderLineSyncProvenance(string? ReferenceCode, int PriceSource);

/// <summary>
/// 折扣 union：none / manual-amount / manual-percent / promotion，
/// 与 iPad SharedLineDiscountStateV1 完全一致，wire 判别字段为 mode（不是 kind），
/// 仅序列化当前 mode 的字段。
/// </summary>
public sealed record SharedHeldOrderDiscountState(
    string Mode,
    long? Cents = null,
    int? BasisPoints = null,
    IReadOnlyList<string>? PromotionIds = null);

public static class SharedHeldOrderCanonicalConstants
{
    public const string SaleMode = "sale";
    public const string LineKindSale = "sale";

    /// <summary>普通共享 sale 只允许 catalog 或 manual；promotion/open-item 拒绝。</summary>
    public const string BasePriceSourceCatalog = "catalog";
    public const string BasePriceSourceManual = "manual";

    public const string DiscountNone = "none";
    public const string DiscountManualAmount = "manual-amount";
    public const string DiscountManualPercent = "manual-percent";
    public const string DiscountPromotion = "promotion";

    // 与 SharedSaleCartV1Constants 一致的边界常量。
    public const long MaxCents = 1_000_000_000_000L;

    /// <summary>三端统一累计金额上限：Number.MAX_SAFE_INTEGER（2^53-1）。</summary>
    public const long MaxTotalCents = 9_007_199_254_740_991L;
    public const decimal MaxQuantity = 1_000_000m;
    public const int MaxLineCount = 1_000;
    public const int MaxPromotionCount = 100;
    public const int MaxPromotionProducts = 100;
    public const int MaxBasisPoints = 10_000;
    public const int MaxCodeLength = 64;
    public const int MaxNameLength = 200;
    public const int MaxReferenceLength = 128;
    public const decimal MaxUnitWeight = 1_000_000m;
}

/// <summary>稳定阻断原因；调用方需据此保留本地挂单并等待规则/输入补齐。</summary>
public sealed record SharedHeldOrderMappingBlock(string Reason, string? Detail);

public sealed record SharedHeldOrderMappingResult(
    SharedHeldOrderCanonicalPayload? Payload,
    SharedHeldOrderMappingBlock? Block)
{
    public bool IsBlocked => Block is not null;
}

public static class SharedHeldOrderMappingReasons
{
    public const string ReturnLine = "ReturnLineNotSupported";
    public const string OpenItemLine = "OpenItemLineNotSupported";
    public const string PromotionRulesMissing = "PromotionRulesMissing";
    public const string PromotionRulesMismatch = "PromotionRulesMismatch";
    public const string CatalogDiscountPromotionConflict = "CatalogDiscountPromotionConflict";
    public const string InvalidSnapshot = "InvalidSnapshot";
}
