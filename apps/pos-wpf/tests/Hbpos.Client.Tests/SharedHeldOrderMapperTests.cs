using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Client.Tests;

/// <summary>
/// SuspendedOrder 普通 sale 快照 -> canonical mapper：
/// 无损映射、return/open-item 拒绝、手工折扣无损、自动促销仅在全量冻结规则精确对应时发布。
/// </summary>
public sealed class SharedHeldOrderMapperTests
{
    private static readonly ISharedHeldOrderMapper Mapper = new SharedHeldOrderMapper();

    private static readonly DateTimeOffset HeldAt = new(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);

    private static SuspendedOrderLine SaleLine(
        string productCode,
        decimal quantity,
        decimal unitPrice,
        decimal discountAmount,
        decimal? discountPercent,
        PosCartLineDiscountSource discountSource = PosCartLineDiscountSource.None,
        CartLineKind kind = CartLineKind.Sale,
        PriceSourceKind priceSource = PriceSourceKind.ProductBase,
        string? referenceCode = null,
        bool isManualPrice = false)
    {
        return new SuspendedOrderLine(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "S001",
            productCode,
            referenceCode,
            $"Product {productCode}",
            $"CODE-{productCode}",
            null,
            null,
            quantity,
            unitPrice,
            discountAmount,
            discountPercent,
            quantity * unitPrice - discountAmount,
            priceSource,
            "ProductBase",
            discountSource)
        {
            Kind = kind,
            IsManualPrice = isManualPrice
        };
    }

    private static SuspendedOrder Order(params SuspendedOrderLine[] lines)
    {
        return new SuspendedOrder(
            Guid.NewGuid(),
            "S001",
            "POS-01",
            "cashier-1",
            "Cashier One",
            HeldAt,
            0m,
            0m,
            0m,
            SuspendedOrderStatus.Pending,
            lines);
    }

    [Fact]
    public void Map_plain_sale_snapshot_is_lossless()
    {
        var order = Order(
            SaleLine("P1", 1, 11.00m, 0m, null, referenceCode: "REF-1"),
            SaleLine("P2", 2, 10.00m, 1.25m, null, PosCartLineDiscountSource.Manual));

        var result = Mapper.Map(order, null, revision: 5);

        Assert.False(result.IsBlocked);
        Assert.NotNull(result.Payload);
        Assert.Equal(1, result.Payload.Version);
        Assert.Equal(5, result.Payload.PricingState.Revision);
        Assert.Equal("sale", result.Payload.PricingState.Mode);
        Assert.Equal("2026-07-28T00:00:00.000Z", result.Payload.PricingState.AsOfIso);
        Assert.Empty(result.Payload.PricingState.Promotions);
        Assert.Equal(2, result.Payload.PricingState.Lines.Count);

        var first = result.Payload.PricingState.Lines[0];
        Assert.Equal(1100L, first.UnitPriceCents);
        Assert.Equal(1m, first.Quantity);
        Assert.Equal("catalog", first.BasePriceSource);
        Assert.Equal("sale", first.Kind);
        Assert.NotNull(first.SyncProvenance);
        Assert.Equal("REF-1", first.SyncProvenance.ReferenceCode);
        Assert.Equal(0, first.SyncProvenance.PriceSource);
        Assert.Equal("none", first.DiscountState.Mode);

        // 手工金额折扣按整数 cents 无损。
        var second = result.Payload.PricingState.Lines[1];
        Assert.Equal(2000L, second.UnitPriceCents);
        Assert.Equal(2m, second.Quantity);
        Assert.Equal("manual-amount", second.DiscountState.Mode);
        Assert.Equal(125L, second.DiscountState.Cents);
        Assert.Null(second.DiscountState.BasisPoints);
    }

    [Fact]
    public void Map_accepts_positive_decimal_weight_quantity()
    {
        // 普通 sale 支持称重小数数量（如 1.5kg），只要求正数，不要求整数。
        var order = Order(
            SaleLine("P1", 1.5m, 12.00m, 0m, null));

        var result = Mapper.Map(order, null, revision: 1);

        Assert.False(result.IsBlocked);
        var line = Assert.Single(result.Payload!.PricingState.Lines);
        Assert.Equal(1.5m, line.Quantity);
        Assert.Equal(1200L, line.UnitPriceCents);
    }

    [Fact]
    public void Map_zero_or_negative_quantity_is_rejected()
    {
        // 契约 validator 要求 quantity > 0；保留正数下限，仅去掉整数限制。
        var zeroOrder = Order(SaleLine("P1", 0m, 12.00m, 0m, null));
        var negativeOrder = Order(SaleLine("P1", -1m, 12.00m, 0m, null));
        var tooLargeOrder = Order(SaleLine(
            "P1",
            SharedHeldOrderCanonicalConstants.MaxQuantity + 0.01m,
            12.00m,
            0m,
            null));

        Assert.Throws<ArgumentException>(() => Mapper.Map(zeroOrder, null, revision: 1));
        Assert.Throws<ArgumentException>(() => Mapper.Map(negativeOrder, null, revision: 1));
        Assert.Throws<ArgumentException>(() => Mapper.Map(tooLargeOrder, null, revision: 1));
    }

    [Fact]
    public void Map_never_leaks_internal_money_or_discount_kind_into_wire()
    {
        var order = Order(
            SaleLine("P1", 1, 11.00m, 1.00m, null, PosCartLineDiscountSource.Promotion));
        var frozenRules = new[]
        {
            new CatalogPromotionRuleDto(
                "PROMO-X",
                "Single save 1",
                true,
                100,
                1,
                10.00m,
                null,
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 31, 23, 59, 59, TimeSpan.Zero),
                null,
                [new CatalogPromotionProductDto("P1", 1)])
        };

        var result = Mapper.Map(order, frozenRules, revision: 1);
        var json = new SharedHeldOrderCanonicalJsonSerializer().Serialize(result.Payload!);

        Assert.Contains("\"fixedPriceCents\":1000", json);
        Assert.Contains("\"mode\":\"promotion\"", json);
        Assert.DoesNotContain("\"fixedPrice\":{", json);
        Assert.DoesNotContain("\"currency\"", json);
        Assert.DoesNotContain("\"kind\":\"promotion\"", json);
        Assert.DoesNotContain("\"originalOrderGuid\":\"", json);
        Assert.DoesNotContain("\"returnSourceKey\":\"", json);
    }

    [Fact]
    public void Map_manual_zero_percent_falls_back_to_lossless_amount()
    {
        // 旧数据：带折扣金额但 percent 为 0，不能发布非法 manual-percent（basisPoints=0），
        // 应回退为 manual-amount 整数 cents，保持金额无损。
        var order = Order(
            SaleLine("P1", 1, 11.00m, 1.00m, 0m, PosCartLineDiscountSource.Manual));

        var result = Mapper.Map(order, null, revision: 1);

        Assert.False(result.IsBlocked);
        var line = result.Payload!.PricingState.Lines[0];
        Assert.Equal("manual-amount", line.DiscountState.Mode);
        Assert.Equal(100L, line.DiscountState.Cents);
        Assert.Null(line.DiscountState.BasisPoints);
    }

    [Fact]
    public void Map_manual_percent_discount_is_lossless()
    {
        var order = Order(
            SaleLine("P1", 2, 10.00m, 2.00m, 10m, PosCartLineDiscountSource.Manual));

        var result = Mapper.Map(order, null, revision: 1);

        Assert.False(result.IsBlocked);
        var line = result.Payload!.PricingState.Lines[0];
        Assert.Equal("manual-percent", line.DiscountState.Mode);
        Assert.Equal(1000, line.DiscountState.BasisPoints);
        Assert.Null(line.DiscountState.Cents);
    }

    [Fact]
    public void Map_manual_percent_not_exactly_representable_falls_back_to_frozen_amount_cents()
    {
        // 10.555% 无法用整数 basisPoints 精确表示（10.555 * 100 = 1055.5），
        // 必须发布冻结的手工金额 cents（211），不能舍入为 1056 bps 静默改变恢复金额。
        var order = Order(
            SaleLine("P1", 2, 10.00m, 2.11m, 10.555m, PosCartLineDiscountSource.Manual));

        var result = Mapper.Map(order, null, revision: 1);

        Assert.False(result.IsBlocked);
        var line = result.Payload!.PricingState.Lines[0];
        Assert.Equal("manual-amount", line.DiscountState.Mode);
        Assert.Equal(211L, line.DiscountState.Cents);
        Assert.Null(line.DiscountState.BasisPoints);
    }

    [Fact]
    public void Map_manual_percent_exactly_representable_stays_percent()
    {
        // 10.55% * 100 = 1055 是整数，可无损用 basisPoints 冻结，不应误回退金额。
        var order = Order(
            SaleLine("P1", 2, 10.00m, 2.11m, 10.55m, PosCartLineDiscountSource.Manual));

        var result = Mapper.Map(order, null, revision: 1);

        Assert.False(result.IsBlocked);
        var line = result.Payload!.PricingState.Lines[0];
        Assert.Equal("manual-percent", line.DiscountState.Mode);
        Assert.Equal(1055, line.DiscountState.BasisPoints);
        Assert.Null(line.DiscountState.Cents);
    }

    [Fact]
    public void Map_return_line_is_rejected()
    {
        var order = Order(
            SaleLine("P1", 1, 11.00m, 0m, null),
            SaleLine("P2", 1, 9.00m, 0m, null, kind: CartLineKind.Return));

        var result = Mapper.Map(order, null, revision: 1);

        Assert.True(result.IsBlocked);
        Assert.Null(result.Payload);
        Assert.Equal(SharedHeldOrderMappingReasons.ReturnLine, result.Block!.Reason);
    }

    [Fact]
    public void Map_open_item_line_is_rejected()
    {
        var order = Order(
            SaleLine("P1", 1, 11.00m, 0m, null, kind: CartLineKind.OpenItem));

        var result = Mapper.Map(order, null, revision: 1);

        Assert.True(result.IsBlocked);
        Assert.Equal(SharedHeldOrderMappingReasons.OpenItemLine, result.Block!.Reason);
    }

    [Fact]
    public void Map_promotion_discount_without_frozen_rules_is_blocked()
    {
        var order = Order(
            SaleLine("P1", 1, 11.00m, 1.00m, null, PosCartLineDiscountSource.Promotion));

        var result = Mapper.Map(order, null, revision: 1);

        Assert.True(result.IsBlocked);
        Assert.Equal(SharedHeldOrderMappingReasons.PromotionRulesMissing, result.Block!.Reason);
    }

    [Fact]
    public void Map_promotion_with_exact_frozen_rules_publishes_promotion_ids()
    {
        var order = Order(
            SaleLine("P1", 1, 11.00m, 1.00m, null, PosCartLineDiscountSource.Promotion),
            SaleLine("P1", 1, 11.00m, 1.00m, null, PosCartLineDiscountSource.Promotion));
        var frozenRules = new[]
        {
            new CatalogPromotionRuleDto(
                "PROMO-X",
                "Buy 2 save 10",
                true,
                100,
                2,
                20.00m,
                null,
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 31, 23, 59, 59, TimeSpan.Zero),
                null,
                [new CatalogPromotionProductDto("P1", 1)])
        };

        var result = Mapper.Map(order, frozenRules, revision: 1);

        Assert.False(result.IsBlocked);
        var promotion = Assert.Single(result.Payload!.PricingState.Promotions);
        Assert.Equal("PROMO-X", promotion.Id);
        Assert.Equal(2000L, promotion.FixedPriceCents);
        Assert.Equal(1m, promotion.Products[0].UnitWeight);
        foreach (var line in result.Payload.PricingState.Lines)
        {
            Assert.Equal("promotion", line.DiscountState.Mode);
            Assert.Equal(100L, line.DiscountState.Cents);
            Assert.Equal(["PROMO-X"], line.DiscountState.PromotionIds);
        }
    }

    [Fact]
    public void Map_promotion_with_frozen_rules_mismatch_is_blocked()
    {
        // 冻结规则应产生每行 $1.00 促销折扣，但挂单只存了 $0.50，无法精确对应。
        var order = Order(
            SaleLine("P1", 1, 11.00m, 0.50m, null, PosCartLineDiscountSource.Promotion),
            SaleLine("P1", 1, 11.00m, 0.50m, null, PosCartLineDiscountSource.Promotion));
        var frozenRules = new[]
        {
            new CatalogPromotionRuleDto(
                "PROMO-X",
                "Buy 2 save 10",
                true,
                100,
                2,
                20.00m,
                null,
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 31, 23, 59, 59, TimeSpan.Zero),
                null,
                [new CatalogPromotionProductDto("P1", 1)])
        };

        var result = Mapper.Map(order, frozenRules, revision: 1);

        Assert.True(result.IsBlocked);
        Assert.Equal(SharedHeldOrderMappingReasons.PromotionRulesMismatch, result.Block!.Reason);
    }

    [Fact]
    public void Map_frozen_rules_producing_discount_on_plain_line_is_blocked()
    {
        // 冻结规则会在普通行上产生促销折扣，但挂单未记录该折扣，同样不能精确对应。
        var order = Order(
            SaleLine("P1", 1, 11.00m, 0m, null),
            SaleLine("P1", 1, 11.00m, 0m, null));
        var frozenRules = new[]
        {
            new CatalogPromotionRuleDto(
                "PROMO-X",
                "Buy 2 save 10",
                true,
                100,
                2,
                20.00m,
                null,
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 31, 23, 59, 59, TimeSpan.Zero),
                null,
                [new CatalogPromotionProductDto("P1", 1)])
        };

        var result = Mapper.Map(order, frozenRules, revision: 1);

        Assert.True(result.IsBlocked);
        Assert.Equal(SharedHeldOrderMappingReasons.PromotionRulesMismatch, result.Block!.Reason);
    }

    [Fact]
    public void Map_manual_price_line_uses_canonical_manual_base_price_source()
    {
        var order = Order(
            SaleLine("P1", 1, 7.70m, 0m, null, priceSource: PriceSourceKind.StoreRetailPrice, isManualPrice: true),
            SaleLine("P2", 1, 9.90m, 0m, null, priceSource: PriceSourceKind.StoreClearancePrice));

        var result = Mapper.Map(order, null, revision: 1);

        Assert.False(result.IsBlocked);
        var lines = result.Payload!.PricingState.Lines;
        Assert.Equal("manual", lines[0].BasePriceSource);
        Assert.Equal("catalog", lines[1].BasePriceSource);
        Assert.Equal(770L, lines[0].UnitPriceCents);
        Assert.Equal(990L, lines[1].UnitPriceCents);
        // 底层目录来源种类仍作为 sync provenance 携带，便于目标端展示。
        Assert.Equal((int)PriceSourceKind.StoreRetailPrice, lines[0].SyncProvenance!.PriceSource);
        Assert.Equal((int)PriceSourceKind.StoreClearancePrice, lines[1].SyncProvenance!.PriceSource);
    }

    [Fact]
    public void Map_outputs_only_frozen_rules_that_contributed_to_promotion_lines()
    {
        var order = Order(
            SaleLine("P1", 2, 11.00m, 2.00m, null, PosCartLineDiscountSource.Promotion));
        var frozenRules = new[]
        {
            new CatalogPromotionRuleDto(
                "PROMO-ACTIVE",
                "Buy 2 save 2",
                true,
                100,
                2,
                20.00m,
                null,
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 31, 23, 59, 59, TimeSpan.Zero),
                null,
                [new CatalogPromotionProductDto("P1", 1)]),
            new CatalogPromotionRuleDto(
                "PROMO-UNRELATED",
                "Unrelated product discount",
                false,
                10,
                1,
                1.00m,
                null,
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 31, 23, 59, 59, TimeSpan.Zero),
                null,
                [new CatalogPromotionProductDto("P2", 1)])
        };

        var result = Mapper.Map(order, frozenRules, revision: 1);

        Assert.False(result.IsBlocked);
        var promotion = Assert.Single(result.Payload!.PricingState.Promotions);
        Assert.Equal("PROMO-ACTIVE", promotion.Id);
        Assert.Equal(["PROMO-ACTIVE"], result.Payload.PricingState.Lines[0].DiscountState.PromotionIds);
    }

    [Fact]
    public void Map_blocks_duplicate_frozen_rule_definition_ids()
    {
        var order = Order(
            SaleLine("P1", 2, 11.00m, 2.00m, null, PosCartLineDiscountSource.Promotion));
        var frozenRules = new[]
        {
            new CatalogPromotionRuleDto(
                "PROMO-DUP",
                "First",
                true,
                100,
                2,
                20.00m,
                null,
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 31, 23, 59, 59, TimeSpan.Zero),
                null,
                [new CatalogPromotionProductDto("P1", 1)]),
            new CatalogPromotionRuleDto(
                "PROMO-DUP",
                "Duplicate id",
                true,
                100,
                2,
                20.00m,
                null,
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 31, 23, 59, 59, TimeSpan.Zero),
                null,
                [new CatalogPromotionProductDto("P1", 1)])
        };

        var result = Mapper.Map(order, frozenRules, revision: 1);

        Assert.True(result.IsBlocked);
        Assert.Equal(SharedHeldOrderMappingReasons.PromotionRulesMismatch, result.Block!.Reason);
        Assert.Contains("PROMO-DUP", result.Block.Detail);
    }
}
