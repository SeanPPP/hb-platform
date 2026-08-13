using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hbpos.Contracts.HeldOrders;

/// <summary>
/// V2 并行契约：结构与冻结的 V1 完全对齐，line 仅新增 catalogDiscountBasisPoints。
/// 本文件不修改 SharedHeldOrderContracts.cs，也不放宽 V1 的任何 wire/validator/fixture。
/// </summary>
public static class SharedSaleCartV2Constants
{
    public const int PayloadVersion = 2;
    public const int MaxCatalogBasisPoints = 10_000;
}

public sealed record SharedSaleCartV2(
    int Version,
    SharedPricingStateV2 PricingState);

public sealed record SharedPricingStateV2(
    int Revision,
    string Mode,
    string AsOfIso,
    IReadOnlyList<SharedPromotionV1> Promotions,
    IReadOnlyList<SharedSaleLineV2> Lines);

public sealed record SharedSaleLineV2(
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
    SharedLineDiscountStateV1 DiscountState,
    [property: JsonRequired]
    int CatalogDiscountBasisPoints = 0);

/// <summary>
/// V2 校验：复用 V1 validator 保证冻结字段逐条一致，再叠加
/// catalogDiscountBasisPoints 0..10000 与 promotion 冲突检查。
/// </summary>
public static class SharedSaleCartV2Validator
{
    public static SharedSaleCartV2 Validate(SharedSaleCartV2 cart)
    {
        var errors = ValidateAll(cart);
        if (errors.Count > 0)
        {
            throw new SharedSaleCartValidationException(string.Join("; ", errors));
        }

        return cart;
    }

    public static IReadOnlyList<string> ValidateAll(SharedSaleCartV2 cart)
    {
        var errors = new List<string>();
        if (cart is null)
        {
            errors.Add("cart is required");
            return errors;
        }

        if (cart.Version != SharedSaleCartV2Constants.PayloadVersion)
        {
            errors.Add("version must be 2");
        }

        var pricing = cart.PricingState;
        if (pricing is null)
        {
            errors.Add("pricingState is required");
            return errors;
        }

        var lines = pricing.Lines ?? [];
        foreach (var line in lines)
        {
            if (line is null)
            {
                errors.Add("line is required");
                continue;
            }

            if (line.CatalogDiscountBasisPoints is < 0 or > SharedSaleCartV2Constants.MaxCatalogBasisPoints)
            {
                errors.Add("line.catalogDiscountBasisPoints must be between 0 and "
                    + SharedSaleCartV2Constants.MaxCatalogBasisPoints);
            }

            if (line.CatalogDiscountBasisPoints > 0 &&
                line.DiscountState is not null &&
                string.Equals(
                    line.DiscountState.Mode,
                    SharedSaleCartV1Constants.DiscountModePromotion,
                    StringComparison.Ordinal))
            {
                errors.Add("line.catalogDiscountBasisPoints must not coexist with promotion discount state");
            }
        }

        // 复用 V1 validator：V2 的 V1 字段必须继续逐条满足冻结契约。
        var v1Errors = SharedSaleCartV1Validator.ValidateAll(ToV1(cart));
        errors.AddRange(v1Errors);
        return errors;
    }

    public static SharedSaleCartV1 ToV1(SharedSaleCartV2 cart)
    {
        return new SharedSaleCartV1(
            SharedSaleCartV1Constants.PayloadVersion,
            new SharedPricingStateV1(
                cart.PricingState.Revision,
                cart.PricingState.Mode,
                cart.PricingState.AsOfIso,
                cart.PricingState.Promotions,
                (cart.PricingState.Lines ?? [])
                    // 无效 JSON 可产生 null 行；保留给 V1 validator 统一报告，
                    // 不能在版本复用层先泄漏 NullReferenceException。
                    .Select(line => line is null ? null! : ToV1Line(line))
                    .ToArray()));
    }

    public static SharedSaleCartV2 ToV2(SharedSaleCartV1 cart)
    {
        return new SharedSaleCartV2(
            SharedSaleCartV2Constants.PayloadVersion,
            new SharedPricingStateV2(
                cart.PricingState.Revision,
                cart.PricingState.Mode,
                cart.PricingState.AsOfIso,
                cart.PricingState.Promotions,
                cart.PricingState.Lines.Select(ToV2Line).ToArray()));
    }

    public static bool HasCatalogBaseline(SharedSaleCartV2 cart) =>
        cart.PricingState.Lines.Any(line => line.CatalogDiscountBasisPoints > 0);

    private static SharedSaleLineV1 ToV1Line(SharedSaleLineV2 line) => new(
        line.LineId,
        line.ProductCode,
        line.ItemNumber,
        line.LookupCode,
        line.DisplayName,
        line.Quantity,
        line.UnitPriceCents,
        line.BasePriceSource,
        line.SyncProvenance,
        line.Kind,
        line.ReturnSourceKey,
        line.OriginalOrderGuid,
        line.OriginalOrderDetailGuid,
        line.DiscountState);

    private static SharedSaleLineV2 ToV2Line(SharedSaleLineV1 line) => new(
        line.LineId,
        line.ProductCode,
        line.ItemNumber,
        line.LookupCode,
        line.DisplayName,
        line.Quantity,
        line.UnitPriceCents,
        line.BasePriceSource,
        line.SyncProvenance,
        line.Kind,
        line.ReturnSourceKey,
        line.OriginalOrderGuid,
        line.OriginalOrderDetailGuid,
        line.DiscountState,
        CatalogDiscountBasisPoints: 0);
}

/// <summary>
/// 版本分派与有损降级门禁：V2 有 catalog baseline 时禁止降级为 V1。
/// </summary>
public static class SharedSaleCartVersioning
{
    public const int PayloadVersionV1 = SharedSaleCartV1Constants.PayloadVersion;
    public const int PayloadVersionV2 = SharedSaleCartV2Constants.PayloadVersion;

    public static int GetPayloadVersion(object payload) => payload switch
    {
        SharedSaleCartV1 => PayloadVersionV1,
        SharedSaleCartV2 => PayloadVersionV2,
        _ => throw new SharedSaleCartValidationException(
            "Shared sale cart payload must be SharedSaleCartV1 or SharedSaleCartV2.")
    };

    public static object Validate(object payload) => payload switch
    {
        SharedSaleCartV1 v1 => SharedSaleCartV1Validator.Validate(v1),
        SharedSaleCartV2 v2 => SharedSaleCartV2Validator.Validate(v2),
        _ => throw new SharedSaleCartValidationException(
            "Shared sale cart payload must be SharedSaleCartV1 or SharedSaleCartV2.")
    };

    public static SharedSaleCartV1 DowngradeToV1(SharedSaleCartV2 cart)
    {
        var validated = SharedSaleCartV2Validator.Validate(cart);
        if (SharedSaleCartV2Validator.HasCatalogBaseline(validated))
        {
            throw new SharedSaleCartValidationException(
                "Cannot downgrade a V2 cart with catalog baseline to V1.");
        }

        return SharedSaleCartV2Validator.ToV1(validated);
    }
}

/// <summary>
/// V2 wire 形状门禁：TS 端要求每行显式 catalogDiscountBasisPoints，
/// System.Text.Json 对缺失 int 会静默补 0，这里按原始 JSON 字段 presence 拒绝。
/// V1 不包含该字段，照旧可读。
/// </summary>
public static class SharedSaleCartV2JsonContract
{
    public static void EnsureCatalogBasisPointsPresent(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !TryGetPropertyAnyCase(root, "pricingState", out var pricingState) ||
            pricingState.ValueKind != JsonValueKind.Object ||
            !TryGetPropertyAnyCase(pricingState, "lines", out var lines) ||
            lines.ValueKind != JsonValueKind.Array)
        {
            // 结构缺失交由 SharedSaleCartV2Validator 报告；此处只负责字段 presence。
            return;
        }

        foreach (var line in lines.EnumerateArray())
        {
            if (line.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!TryGetPropertyAnyCase(line, "catalogDiscountBasisPoints", out _))
            {
                throw new JsonException(
                    "Shared sale cart V2 line is missing catalogDiscountBasisPoints.");
            }
        }
    }

    private static bool TryGetPropertyAnyCase(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
