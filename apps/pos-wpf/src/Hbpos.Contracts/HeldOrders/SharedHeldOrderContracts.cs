using System.Text.Json.Serialization;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Contracts.HeldOrders;

/// <summary>
/// 冻结的 SharedSaleCartV1 契约：版本、边界常量、枚举与 DTO 定义。
/// 本阶段仅支持 sale 模式，return/open-item 一律拒绝。
/// </summary>
public static class SharedSaleCartV1Constants
{
    public const int PayloadVersion = 1;
    public const string PricingModeSale = "sale";
    public const string LineKindSale = "sale";

    /// <summary>
    /// basePriceSource 与 iPad PricingCartLineState 一致：普通共享 sale
    /// 仅允许 catalog/manual，promotion/open-item 一律拒绝。
    /// </summary>
    public const string PriceSourceCatalog = "catalog";
    public const string PriceSourceManual = "manual";

    public const string DiscountModeNone = "none";
    public const string DiscountModeManualAmount = "manual-amount";
    public const string DiscountModeManualPercent = "manual-percent";
    public const string DiscountModePromotion = "promotion";

    /// <summary>所有金额单位统一为分，上限 100 亿元，防止溢出。 </summary>
    public const long MaxCents = 1_000_000_000_000L;

    /// <summary>
    /// 三端统一累计金额上限：Number.MAX_SAFE_INTEGER（2^53-1）。
    /// 单行 rounded gross 与所有行 rounded gross 合计均不得超过该值。
    /// </summary>
    public const long MaxTotalCents = 9_007_199_254_740_991L;

    public const int MaxQuantity = 1_000_000;
    public const int MaxLineCount = 1_000;
    public const int MaxPromotionCount = 100;
    public const int MaxPromotionProducts = 100;
    public const int MaxBasisPoints = 10_000;
    public const int MaxCodeLength = 64;
    public const int MaxNameLength = 200;
    public const int MaxReferenceLength = 128;
    public const decimal MaxUnitWeight = 1_000_000m;
}

public sealed record SharedSaleCartV1(
    int Version,
    SharedPricingStateV1 PricingState);

public sealed record SharedPricingStateV1(
    int Revision,
    string Mode,
    string AsOfIso,
    IReadOnlyList<SharedPromotionV1> Promotions,
    IReadOnlyList<SharedSaleLineV1> Lines);

public sealed record SharedPromotionV1(
    string Id,
    string Name,
    string EffectiveStartIso,
    string EffectiveEndIso,
    bool IsExclusive,
    int Priority,
    int ApplyQuantity,
    long FixedPriceCents,
    int? MaxApplicationsPerOrder,
    IReadOnlyList<SharedPromotionProductV1> Products);

public sealed record SharedPromotionProductV1(
    string ProductCode,
    decimal UnitWeight);

public sealed record SharedSaleLineV1(
    string LineId,
    string ProductCode,
    string? ItemNumber,
    string LookupCode,
    string DisplayName,
    decimal Quantity,
    long UnitPriceCents,
    string BasePriceSource,
    SharedLineSyncProvenanceV1? SyncProvenance,
    string Kind,
    string? ReturnSourceKey,
    Guid? OriginalOrderGuid,
    Guid? OriginalOrderDetailGuid,
    SharedLineDiscountStateV1 DiscountState);

public sealed record SharedLineSyncProvenanceV1(
    string? ReferenceCode,
    PriceSourceKind PriceSource);

public sealed record SharedLineDiscountStateV1(
    string Mode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? Cents = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? BasisPoints = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? PromotionIds = null);

public sealed class SharedSaleCartValidationException(string message)
    : Exception(message);

/// <summary>
/// SharedSaleCartV1 的 canonical 校验：所有 cents/int 有界、拒绝负值、
/// 拒绝重复 lineId/促销 id、拒绝非 sale/return/open-item 以及非空 return/original 字段。
/// </summary>
public static class SharedSaleCartV1Validator
{
    public static SharedSaleCartV1 Validate(SharedSaleCartV1 cart)
    {
        var errors = ValidateAll(cart);
        if (errors.Count > 0)
        {
            throw new SharedSaleCartValidationException(string.Join("; ", errors));
        }

        return cart;
    }

    public static IReadOnlyList<string> ValidateAll(SharedSaleCartV1 cart)
    {
        var errors = new List<string>();
        if (cart is null)
        {
            errors.Add("cart is required");
            return errors;
        }

        if (cart.Version != SharedSaleCartV1Constants.PayloadVersion)
        {
            errors.Add("version must be 1");
        }

        var pricing = cart.PricingState;
        if (pricing is null)
        {
            errors.Add("pricingState is required");
            return errors;
        }

        if (pricing.Revision is < 1 or > SharedSaleCartV1Constants.MaxQuantity)
        {
            errors.Add("pricingState.revision must be between 1 and " + SharedSaleCartV1Constants.MaxQuantity);
        }

        if (!string.Equals(pricing.Mode, SharedSaleCartV1Constants.PricingModeSale, StringComparison.Ordinal))
        {
            errors.Add("pricingState.mode must be 'sale'");
        }

        if (!TryParseUtc(pricing.AsOfIso, out _))
        {
            errors.Add("pricingState.asOfIso must be a UTC ISO-8601 timestamp");
        }

        var promotions = pricing.Promotions ?? [];
        if (promotions.Count > SharedSaleCartV1Constants.MaxPromotionCount)
        {
            errors.Add("pricingState.promotions must not exceed "
                + SharedSaleCartV1Constants.MaxPromotionCount + " items");
        }

        var promotionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var promotion in promotions)
        {
            if (promotion is null)
            {
                errors.Add("promotion is required");
                continue;
            }

            if (string.IsNullOrWhiteSpace(promotion.Id) ||
                promotion.Id.Length > SharedSaleCartV1Constants.MaxCodeLength)
            {
                errors.Add("promotion.id must be a non-empty code up to "
                    + SharedSaleCartV1Constants.MaxCodeLength + " characters");
            }
            else if (!promotionIds.Add(promotion.Id))
            {
                errors.Add("promotion id must be unique: " + promotion.Id);
            }

            if (string.IsNullOrWhiteSpace(promotion.Name) ||
                promotion.Name.Length > SharedSaleCartV1Constants.MaxNameLength)
            {
                errors.Add("promotion.name must be a non-empty name up to "
                    + SharedSaleCartV1Constants.MaxNameLength + " characters");
            }

            var startParsed = TryParseUtc(promotion.EffectiveStartIso, out var start);
            var endParsed = TryParseUtc(promotion.EffectiveEndIso, out var end);
            if (!startParsed)
            {
                errors.Add("promotion.effectiveStartIso must be a UTC ISO-8601 timestamp");
            }

            if (!endParsed)
            {
                errors.Add("promotion.effectiveEndIso must be a UTC ISO-8601 timestamp");
            }
            else if (startParsed && end < start)
            {
                errors.Add("promotion.effectiveEndIso must not be earlier than effectiveStartIso");
            }

            if (promotion.ApplyQuantity is < 1 or > SharedSaleCartV1Constants.MaxQuantity)
            {
                errors.Add("promotion.applyQuantity must be between 1 and "
                    + SharedSaleCartV1Constants.MaxQuantity);
            }

            if (promotion.FixedPriceCents is < 0 or > SharedSaleCartV1Constants.MaxCents)
            {
                errors.Add("promotion.fixedPriceCents must be between 0 and "
                    + SharedSaleCartV1Constants.MaxCents);
            }

            if (promotion.MaxApplicationsPerOrder is not null and (< 1 or > SharedSaleCartV1Constants.MaxQuantity))
            {
                errors.Add("promotion.maxApplicationsPerOrder must be null or between 1 and "
                    + SharedSaleCartV1Constants.MaxQuantity);
            }

            if (promotion.Priority is < 0 or > SharedSaleCartV1Constants.MaxQuantity)
            {
                errors.Add("promotion.priority must be between 0 and "
                    + SharedSaleCartV1Constants.MaxQuantity);
            }

            var products = promotion.Products ?? [];
            if (products.Count is < 1 or > SharedSaleCartV1Constants.MaxPromotionProducts)
            {
                errors.Add("promotion.products must contain 1 to "
                    + SharedSaleCartV1Constants.MaxPromotionProducts + " items");
            }

            foreach (var product in products)
            {
                if (product is null)
                {
                    errors.Add("promotion product is required");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(product.ProductCode) ||
                    product.ProductCode.Length > SharedSaleCartV1Constants.MaxCodeLength)
                {
                    errors.Add("promotion productCode must be a non-empty code up to "
                        + SharedSaleCartV1Constants.MaxCodeLength + " characters");
                }

                if (product.UnitWeight is < 0 or > SharedSaleCartV1Constants.MaxUnitWeight)
                {
                    errors.Add("promotion product unitWeight must be between 0 and "
                        + SharedSaleCartV1Constants.MaxUnitWeight);
                }
            }
        }

        var lines = pricing.Lines ?? [];
        if (lines.Count is < 1 or > SharedSaleCartV1Constants.MaxLineCount)
        {
            errors.Add("pricingState.lines must contain 1 to "
                + SharedSaleCartV1Constants.MaxLineCount + " items");
        }

        var lineIds = new HashSet<string>(StringComparer.Ordinal);
        long totalGrossCents = 0;
        foreach (var line in lines)
        {
            var grossCents = ValidateLine(line, lineIds, promotionIds, errors);
            if (grossCents is { } gross)
            {
                // 先比较再累加：total 恒 <= MaxTotalCents，long 不溢出、结果精确。
                if (totalGrossCents > SharedSaleCartV1Constants.MaxTotalCents - gross)
                {
                    errors.Add("pricingState lines rounded gross total must not exceed "
                        + SharedSaleCartV1Constants.MaxTotalCents);
                }
                else
                {
                    totalGrossCents += gross;
                }
            }
        }

        return errors;
    }

    private static long? ValidateLine(
        SharedSaleLineV1? line,
        HashSet<string> lineIds,
        HashSet<string> promotionIds,
        List<string> errors)
    {
        if (line is null)
        {
            errors.Add("line is required");
            return null;
        }

        if (string.IsNullOrWhiteSpace(line.LineId) ||
            line.LineId.Length > SharedSaleCartV1Constants.MaxCodeLength)
        {
            errors.Add("line.lineId must be a non-empty code up to "
                + SharedSaleCartV1Constants.MaxCodeLength + " characters");
        }
        else if (!lineIds.Add(line.LineId))
        {
            errors.Add("line.lineId must be unique: " + line.LineId);
        }

        if (string.IsNullOrWhiteSpace(line.ProductCode) ||
            line.ProductCode.Length > SharedSaleCartV1Constants.MaxCodeLength)
        {
            errors.Add("line.productCode must be a non-empty code up to "
                + SharedSaleCartV1Constants.MaxCodeLength + " characters");
        }

        if (line.ItemNumber is { Length: > SharedSaleCartV1Constants.MaxCodeLength })
        {
            errors.Add("line.itemNumber must not exceed "
                + SharedSaleCartV1Constants.MaxCodeLength + " characters");
        }

        if (string.IsNullOrWhiteSpace(line.LookupCode) ||
            line.LookupCode.Length > SharedSaleCartV1Constants.MaxCodeLength)
        {
            errors.Add("line.lookupCode must be a non-empty code up to "
                + SharedSaleCartV1Constants.MaxCodeLength + " characters");
        }

        if (string.IsNullOrWhiteSpace(line.DisplayName) ||
            line.DisplayName.Length > SharedSaleCartV1Constants.MaxNameLength)
        {
            errors.Add("line.displayName must be a non-empty name up to "
                + SharedSaleCartV1Constants.MaxNameLength + " characters");
        }

        if (line.Quantity is <= 0 or > SharedSaleCartV1Constants.MaxQuantity)
        {
            errors.Add("line.quantity must be between 0 and "
                + SharedSaleCartV1Constants.MaxQuantity);
        }

        if (line.UnitPriceCents is < 0 or > SharedSaleCartV1Constants.MaxCents)
        {
            errors.Add("line.unitPriceCents must be between 0 and "
                + SharedSaleCartV1Constants.MaxCents);
        }

        decimal? gross = null;
        if (line.Quantity is > 0 and <= SharedSaleCartV1Constants.MaxQuantity
            && line.UnitPriceCents is >= 0 and <= SharedSaleCartV1Constants.MaxCents)
        {
            gross = decimal.Round(
                line.Quantity * line.UnitPriceCents,
                0,
                MidpointRounding.AwayFromZero);
            if (gross > SharedSaleCartV1Constants.MaxTotalCents)
            {
                errors.Add("line rounded gross must not exceed "
                    + SharedSaleCartV1Constants.MaxTotalCents);
                gross = null;
            }
        }

        if (line.BasePriceSource is not (SharedSaleCartV1Constants.PriceSourceCatalog
            or SharedSaleCartV1Constants.PriceSourceManual))
        {
            errors.Add(
                "line.basePriceSource must be 'catalog' or 'manual'; promotion/open-item are not supported");
        }

        if (!string.Equals(line.Kind, SharedSaleCartV1Constants.LineKindSale, StringComparison.Ordinal))
        {
            errors.Add("line.kind must be 'sale'; return/open-item are not supported");
        }

        if (line.ReturnSourceKey is not null)
        {
            errors.Add("line.returnSourceKey must be null in the frozen SharedSaleCartV1 contract");
        }

        if (line.OriginalOrderGuid is not null)
        {
            errors.Add("line.originalOrderGuid must be null in the frozen SharedSaleCartV1 contract");
        }

        if (line.OriginalOrderDetailGuid is not null)
        {
            errors.Add("line.originalOrderDetailGuid must be null in the frozen SharedSaleCartV1 contract");
        }

        if (line.SyncProvenance is not null)
        {
            if (line.SyncProvenance.ReferenceCode is
                { Length: > SharedSaleCartV1Constants.MaxReferenceLength })
            {
                errors.Add("line.syncProvenance.referenceCode must not exceed "
                    + SharedSaleCartV1Constants.MaxReferenceLength + " characters");
            }

            if (!Enum.IsDefined(line.SyncProvenance.PriceSource))
            {
                errors.Add("line.syncProvenance.priceSource is not a supported price source");
            }
        }

        ValidateDiscountState(
            line.DiscountState,
            gross,
            promotionIds,
            errors);

        return gross is { } lineGross ? (long)lineGross : null;
    }

    private static void ValidateDiscountState(
        SharedLineDiscountStateV1? discount,
        decimal? gross,
        HashSet<string> promotionIds,
        List<string> errors)
    {
        if (discount is null)
        {
            errors.Add("line.discountState is required");
            return;
        }

        switch (discount.Mode)
        {
            case SharedSaleCartV1Constants.DiscountModeNone:
                if (discount.Cents is not null ||
                    discount.BasisPoints is not null ||
                    discount.PromotionIds is not null)
                {
                    errors.Add("discountState none must not carry cents, basisPoints or promotionIds");
                }

                break;
            case SharedSaleCartV1Constants.DiscountModeManualAmount:
                if (discount.Cents is null || discount.Cents is < 0 or > SharedSaleCartV1Constants.MaxCents)
                {
                    errors.Add("discountState manual-amount requires cents between 0 and "
                        + SharedSaleCartV1Constants.MaxCents);
                }

                if (discount.Cents is { } manualAmountCents
                    && gross is { } lineGross
                    && manualAmountCents > lineGross)
                {
                    errors.Add("discountState manual-amount cents must not exceed rounded line gross");
                }

                if (discount.BasisPoints is not null || discount.PromotionIds is not null)
                {
                    errors.Add("discountState manual-amount must not carry basisPoints or promotionIds");
                }

                break;
            case SharedSaleCartV1Constants.DiscountModeManualPercent:
                if (discount.BasisPoints is null or < 1 or > SharedSaleCartV1Constants.MaxBasisPoints)
                {
                    errors.Add("discountState manual-percent requires basisPoints between 1 and "
                        + SharedSaleCartV1Constants.MaxBasisPoints);
                }

                if (discount.Cents is not null || discount.PromotionIds is not null)
                {
                    errors.Add("discountState manual-percent must not carry cents or promotionIds");
                }

                break;
            case SharedSaleCartV1Constants.DiscountModePromotion:
                if (discount.Cents is null || discount.Cents is < 0 or > SharedSaleCartV1Constants.MaxCents)
                {
                    errors.Add("discountState promotion requires cents between 0 and "
                        + SharedSaleCartV1Constants.MaxCents);
                }

                if (discount.Cents is { } promotionAmountCents
                    && gross is { } promotionGross
                    && promotionAmountCents > promotionGross)
                {
                    errors.Add("discountState promotion cents must not exceed rounded line gross");
                }

                var discountPromotionIds = discount.PromotionIds ?? [];
                if (discountPromotionIds.Count == 0)
                {
                    errors.Add("discountState promotion requires non-empty promotionIds");
                }
                else if (discountPromotionIds.Distinct(StringComparer.Ordinal).Count()
                    != discountPromotionIds.Count)
                {
                    errors.Add("discountState promotionIds must be unique");
                }

                if (discountPromotionIds.Any(id => string.IsNullOrWhiteSpace(id) ||
                    id.Length > SharedSaleCartV1Constants.MaxCodeLength))
                {
                    errors.Add("discountState promotionIds must be non-empty codes up to "
                        + SharedSaleCartV1Constants.MaxCodeLength + " characters");
                }

                foreach (var promotionId in discountPromotionIds)
                {
                    if (!promotionIds.Contains(promotionId))
                    {
                        errors.Add(
                            "discountState promotionIds must reference frozen promotions: " + promotionId);
                    }
                }

                if (discount.BasisPoints is not null)
                {
                    errors.Add("discountState promotion must not carry basisPoints");
                }

                break;
            default:
                errors.Add("discountState.mode is not supported: " + discount.Mode);
                break;
        }
    }

    private static bool TryParseUtc(string? value, out DateTimeOffset parsed)
    {
        if (!DateTimeOffset.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out parsed))
        {
            parsed = default;
            return false;
        }

        return parsed.Offset == TimeSpan.Zero;
    }
}
