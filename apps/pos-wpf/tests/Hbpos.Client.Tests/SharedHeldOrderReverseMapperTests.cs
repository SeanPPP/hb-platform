using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using static Hbpos.Client.Tests.SharedHeldOrderClientTestSupport;

namespace Hbpos.Client.Tests;

/// <summary>
/// SharedSaleCartV1 canonical -> WPF sale snapshot 反向映射：
/// unit price/provenance/manual 金额与百分比/promotion 折扣来源精确还原；
/// 只允许 kind=sale；金额按 AwayFromZero cents 精确。
/// </summary>
public sealed class SharedHeldOrderReverseMapperTests
{
    private static readonly ISharedHeldOrderReverseMapper Mapper = new SharedHeldOrderReverseMapper();

    [Fact]
    public void Map_plain_sale_restores_unit_price_provenance_and_decimal_quantity()
    {
        var snapshot = Mapper.Map(SampleCanonical(quantity: 1.5m, unitPriceCents: 123456), "S001");

        var line = Assert.Single(snapshot.Lines);
        Assert.Equal(CartLineKind.Sale, line.Kind);
        Assert.Equal(1.5m, line.Quantity);
        Assert.Equal(1234.56m, line.UnitPrice);
        Assert.Equal(PriceSourceKind.StoreRetailPrice, line.PriceSource);
        Assert.Equal("Store Retail Price", line.PriceSourceLabel);
        Assert.Equal("REF-1", line.ReferenceCode);
        Assert.Equal("P-1", line.ProductCode);
        Assert.Equal("ITEM-1", line.ItemNumber);
        Assert.Equal("CODE-1", line.LookupCode);
        Assert.Equal("Product 1", line.DisplayName);
        Assert.Equal("S001", line.StoreCode);
        Assert.Equal(PosCartLineDiscountSource.None, line.DiscountSource);
        Assert.Equal(0m, line.DiscountAmount);
        Assert.False(line.IsManualPrice);
    }

    [Fact]
    public void Map_manual_amount_discount_is_exact_to_the_cent()
    {
        var snapshot = Mapper.Map(
            SampleCanonical(
                quantity: 3m,
                unitPriceCents: 1_000_000_000_000,
                discountMode: SharedHeldOrderCanonicalConstants.DiscountManualAmount,
                discountCents: 250_000_000_000),
            "S001");

        var line = Assert.Single(snapshot.Lines);
        Assert.Equal(10_000_000_000m, line.UnitPrice);
        Assert.Equal(2_500_000_000m, line.DiscountAmount);
        Assert.Equal(PosCartLineDiscountSource.Manual, line.DiscountSource);
        Assert.Null(line.DiscountPercent);
    }

    [Fact]
    public void Map_manual_percent_discount_restores_percent_and_exact_amount()
    {
        var snapshot = Mapper.Map(
            SampleCanonical(
                quantity: 2m,
                unitPriceCents: 1999,
                discountMode: SharedHeldOrderCanonicalConstants.DiscountManualPercent,
                basisPoints: 1234),
            "S001");

        var line = Assert.Single(snapshot.Lines);
        Assert.Equal(12.34m, line.DiscountPercent);
        Assert.Equal(4.93m, line.DiscountAmount);
        Assert.Equal(PosCartLineDiscountSource.Manual, line.DiscountSource);
    }

    [Fact]
    public void Map_catalog_discount_restores_jm006_amount_source_and_baseline()
    {
        var snapshot = Mapper.Map(
            SampleCanonical(
                quantity: 1m,
                unitPriceCents: 699,
                catalogDiscountBasisPoints: 2000),
            "S001");

        var line = Assert.Single(snapshot.Lines);
        Assert.Equal(6.99m, line.UnitPrice);
        Assert.Equal(1.40m, line.DiscountAmount);
        Assert.Equal(20m, line.DiscountPercent);
        Assert.Equal(PosCartLineDiscountSource.Catalog, line.DiscountSource);
        Assert.Equal(2000, line.CatalogDiscountBasisPoints);
    }

    [Fact]
    public void Map_manual_discount_temporarily_overrides_catalog_baseline()
    {
        var snapshot = Mapper.Map(
            SampleCanonical(
                quantity: 1m,
                unitPriceCents: 699,
                discountMode: SharedHeldOrderCanonicalConstants.DiscountManualAmount,
                discountCents: 200,
                catalogDiscountBasisPoints: 2000),
            "S001");

        var line = Assert.Single(snapshot.Lines);
        Assert.Equal(2m, line.DiscountAmount);
        Assert.Equal(PosCartLineDiscountSource.Manual, line.DiscountSource);
        Assert.Equal(2000, line.CatalogDiscountBasisPoints);
    }

    [Fact]
    public void Map_promotion_discount_restores_promotion_source_and_amount()
    {
        var snapshot = Mapper.Map(
            SampleCanonical(
                quantity: 1m,
                unitPriceCents: 5000,
                discountMode: SharedHeldOrderCanonicalConstants.DiscountPromotion,
                discountCents: 1000),
            "S001");

        var line = Assert.Single(snapshot.Lines);
        Assert.Equal(10m, line.DiscountAmount);
        Assert.Equal(PosCartLineDiscountSource.Promotion, line.DiscountSource);
        Assert.Null(line.DiscountPercent);
    }

    [Fact]
    public void Map_manual_base_price_without_provenance_keeps_manual_label()
    {
        var payload = new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                1,
                SharedHeldOrderCanonicalConstants.SaleMode,
                "2026-07-28T00:00:00.000Z",
                [],
                [
                    new SharedHeldOrderPricingLine(
                        "line-1",
                        "P-1",
                        null,
                        "CODE-1",
                        "Product 1",
                        1m,
                        999,
                        SharedHeldOrderCanonicalConstants.BasePriceSourceManual,
                        null,
                        SharedHeldOrderCanonicalConstants.LineKindSale,
                        null,
                        null,
                        null,
                        new SharedHeldOrderDiscountState(SharedHeldOrderCanonicalConstants.DiscountNone))
                ]));

        var line = Assert.Single(Mapper.Map(payload, "S001").Lines);

        Assert.Equal(9.99m, line.UnitPrice);
        Assert.Equal(PriceSourceKind.ProductBase, line.PriceSource);
        Assert.Equal("Manual Price", line.PriceSourceLabel);
        Assert.Null(line.ReferenceCode);
        Assert.True(line.IsManualPrice);
    }

    [Fact]
    public void Map_rejects_non_sale_mode_and_non_sale_kind()
    {
        var wrongMode = SampleCanonical() with
        {
            PricingState = SampleCanonical().PricingState with { Mode = "return" }
        };
        Assert.Throws<SharedHeldOrderReverseMappingException>(() => Mapper.Map(wrongMode, "S001"));

        var wrongKind = SampleCanonical() with
        {
            PricingState = SampleCanonical().PricingState with
            {
                Lines =
                [
                    SampleCanonical().PricingState.Lines[0] with { Kind = "open-item" }
                ]
            }
        };
        Assert.Throws<SharedHeldOrderReverseMappingException>(() => Mapper.Map(wrongKind, "S001"));
    }

    [Fact]
    public void Map_rejects_zero_or_negative_quantity_and_negative_price()
    {
        Assert.Throws<SharedHeldOrderReverseMappingException>(
            () => Mapper.Map(SampleCanonical(quantity: 0m), "S001"));
        Assert.Throws<SharedHeldOrderReverseMappingException>(
            () => Mapper.Map(SampleCanonical(quantity: -1m), "S001"));
        Assert.Throws<SharedHeldOrderReverseMappingException>(
            () => Mapper.Map(SampleCanonical(unitPriceCents: -1), "S001"));
    }

    [Fact]
    public void Map_preserves_frozen_promotion_facts_in_payload_and_sets_promotion_source()
    {
        var payload = new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                2,
                SharedHeldOrderCanonicalConstants.SaleMode,
                "2026-07-28T00:00:00.000Z",
                [
                    new SharedHeldOrderPromotionDefinition(
                        "PROMO-1",
                        "Buy 2 Save 5",
                        "2026-07-01T00:00:00.000Z",
                        "2026-12-31T00:00:00.000Z",
                        false,
                        10,
                        2,
                        1500,
                        1,
                        [new SharedHeldOrderPromotionProduct("P-1", 1m)])
                ],
                [
                    new SharedHeldOrderPricingLine(
                        "line-1",
                        "P-1",
                        null,
                        "CODE-1",
                        "Product 1",
                        2m,
                        1000,
                        SharedHeldOrderCanonicalConstants.BasePriceSourceCatalog,
                        new SharedHeldOrderLineSyncProvenance("REF-1", (int)PriceSourceKind.ProductBase),
                        SharedHeldOrderCanonicalConstants.LineKindSale,
                        null,
                        null,
                        null,
                        new SharedHeldOrderDiscountState(
                            SharedHeldOrderCanonicalConstants.DiscountPromotion,
                            Cents: 500,
                            PromotionIds: ["PROMO-1"]))
                ]));

        var line = Assert.Single(Mapper.Map(payload, "S001").Lines);

        Assert.Equal(PosCartLineDiscountSource.Promotion, line.DiscountSource);
        Assert.Equal(5m, line.DiscountAmount);
        // 冻结促销定义与行级 promotionIds 仍保留在 durable canonical（不写入快照，避免污染购物车）。
        Assert.Equal("PROMO-1", payload.PricingState.Promotions[0].Id);
        Assert.Equal("PROMO-1", Assert.Single(payload.PricingState.Lines[0].DiscountState.PromotionIds!));
    }
}
