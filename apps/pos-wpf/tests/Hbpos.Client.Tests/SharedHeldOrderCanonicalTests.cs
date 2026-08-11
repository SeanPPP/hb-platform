using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

/// <summary>
/// canonical JSON 必须与冻结 SharedSaleCartV1 wire 逐字节一致：
/// camelCase、fixedPriceCents 标量（无 fixedPrice/currency）、discountState 用 mode
/// （无 kind）、quantity/unitWeight 为 JSON number、unitPriceCents long、strict 校验。
/// </summary>
public sealed class SharedHeldOrderCanonicalTests
{
    private static readonly ISharedHeldOrderCanonicalSerializer Serializer =
        new SharedHeldOrderCanonicalJsonSerializer();

    [Fact]
    public void Serialize_matches_frozen_shared_sale_cart_wire_exactly()
    {
        var payload = new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                4,
                "sale",
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
                        1100L,
                        "catalog",
                        null,
                        "sale",
                        null,
                        null,
                        null,
                        new SharedHeldOrderDiscountState("none"))
                ]));

        var json = Serializer.Serialize(payload);

        // 与 iPad shared-sale-cart-v1.test.ts validCart() 的 JSON.stringify 形状一致。
        Assert.Equal(
            """{"version":1,"pricingState":{"revision":4,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[],"lines":[{"lineId":"line-1","productCode":"P-1","itemNumber":null,"lookupCode":"CODE-1","displayName":"Product 1","quantity":1,"unitPriceCents":1100,"basePriceSource":"catalog","syncProvenance":null,"kind":"sale","returnSourceKey":null,"originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"none"}}]}}""",
            json);
        Assert.DoesNotContain("\"fixedPrice\"", json);
        Assert.DoesNotContain("\"currency\"", json);
        Assert.DoesNotContain("\"kind\":\"none\"", json);
    }

    [Fact]
    public void Serialize_includes_sync_provenance_discount_union_and_frozen_promotions_exactly()
    {
        var payload = new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                7,
                "sale",
                "2026-07-28T00:00:00.000Z",
                [
                    new SharedHeldOrderPromotionDefinition(
                        "PROMO-1",
                        "Buy 2 save 10",
                        "2026-07-01T00:00:00.000Z",
                        "2026-07-31T23:59:59.000Z",
                        true,
                        100,
                        2,
                        2000L,
                        1,
                        [new SharedHeldOrderPromotionProduct("P-1", 1m)])
                ],
                [
                    new SharedHeldOrderPricingLine(
                        "line-1",
                        "P-1",
                        "I-1",
                        "CODE-1",
                        "Product 1",
                        2m,
                        1100L,
                        "catalog",
                        new SharedHeldOrderLineSyncProvenance("REF-1", 3),
                        "sale",
                        null,
                        null,
                        null,
                        new SharedHeldOrderDiscountState("promotion", Cents: 200L, PromotionIds: ["PROMO-1"])),
                    new SharedHeldOrderPricingLine(
                        "line-2",
                        "P-2",
                        null,
                        "CODE-2",
                        "Product 2",
                        1m,
                        900L,
                        "catalog",
                        new SharedHeldOrderLineSyncProvenance(null, 0),
                        "sale",
                        null,
                        null,
                        null,
                        new SharedHeldOrderDiscountState("manual-percent", BasisPoints: 1000))
                ]));

        var json = Serializer.Serialize(payload);

        Assert.Equal(
            """{"version":1,"pricingState":{"revision":7,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[{"id":"PROMO-1","name":"Buy 2 save 10","effectiveStartIso":"2026-07-01T00:00:00.000Z","effectiveEndIso":"2026-07-31T23:59:59.000Z","isExclusive":true,"priority":100,"applyQuantity":2,"fixedPriceCents":2000,"maxApplicationsPerOrder":1,"products":[{"productCode":"P-1","unitWeight":1}]}],"lines":[{"lineId":"line-1","productCode":"P-1","itemNumber":"I-1","lookupCode":"CODE-1","displayName":"Product 1","quantity":2,"unitPriceCents":1100,"basePriceSource":"catalog","syncProvenance":{"referenceCode":"REF-1","priceSource":3},"kind":"sale","returnSourceKey":null,"originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"promotion","cents":200,"promotionIds":["PROMO-1"]}},{"lineId":"line-2","productCode":"P-2","itemNumber":null,"lookupCode":"CODE-2","displayName":"Product 2","quantity":1,"unitPriceCents":900,"basePriceSource":"catalog","syncProvenance":{"referenceCode":null,"priceSource":0},"kind":"sale","returnSourceKey":null,"originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"manual-percent","basisPoints":1000}}]}}""",
            json);
        Assert.Contains("\"fixedPriceCents\":2000", json);
        Assert.Contains("\"mode\":\"promotion\"", json);
        Assert.DoesNotContain("\"fixedPrice\":{", json);
        Assert.DoesNotContain("\"currency\"", json);
        Assert.DoesNotContain("\"kind\":\"promotion\"", json);
        Assert.DoesNotContain("\"kind\":\"manual-percent\"", json);
    }

    [Fact]
    public void Serialize_deserialize_round_trip_is_stable_and_strict()
    {
        var payload = new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                2,
                "sale",
                "2026-07-28T08:00:00.000Z",
                [],
                [
                    new SharedHeldOrderPricingLine(
                        "line-9",
                        "P-9",
                        null,
                        "CODE-9",
                        "Nine",
                        1m,
                        999L,
                        "catalog",
                        null,
                        "sale",
                        null,
                        null,
                        null,
                        new SharedHeldOrderDiscountState("manual-amount", Cents: 125L))
                ]));

        var roundTripped = Serializer.Deserialize(Serializer.Serialize(payload));
        var serialized = Serializer.Serialize(roundTripped);

        Assert.Equal(Serializer.Serialize(payload), serialized);
        Assert.Equal(1, roundTripped.Version);
        Assert.Equal(2, roundTripped.PricingState.Revision);
        Assert.Equal("manual-amount", roundTripped.PricingState.Lines[0].DiscountState.Mode);
        Assert.Equal(125L, roundTripped.PricingState.Lines[0].DiscountState.Cents);
    }

    [Fact]
    public void Deserialize_accepts_decimal_quantity_and_unit_weight()
    {
        var json =
            """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[{"id":"PROMO-1","name":"n","effectiveStartIso":"2026-07-01T00:00:00.000Z","effectiveEndIso":"2026-07-31T00:00:00.000Z","isExclusive":false,"priority":1,"applyQuantity":2,"fixedPriceCents":1000,"maxApplicationsPerOrder":null,"products":[{"productCode":"P-1","unitWeight":0.25}]}],"lines":[{"lineId":"l","productCode":"P-1","itemNumber":null,"lookupCode":"c","displayName":"n","quantity":1.5,"unitPriceCents":100,"basePriceSource":"catalog","syncProvenance":null,"kind":"sale","returnSourceKey":null,"originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"none"}}]}}""";

        var payload = Serializer.Deserialize(json);

        Assert.Equal(1.5m, payload.PricingState.Lines[0].Quantity);
        Assert.Equal(0.25m, payload.PricingState.Promotions[0].Products[0].UnitWeight);
    }

    [Theory]
    [InlineData(
        """{"version":2,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[],"lines":[]}}""",
        "payload.version 必须是 1")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":0,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[],"lines":[{"lineId":"l","productCode":"p","itemNumber":null,"lookupCode":"c","displayName":"n","quantity":1,"unitPriceCents":100,"basePriceSource":"catalog","syncProvenance":null,"kind":"sale","returnSourceKey":null,"originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"none"}}]}}""",
        "revision 必须是 1")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":1,"mode":"return","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[],"lines":[]}}""",
        "挂单 canonical 只允许 sale 模式")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[],"lines":[]}}""",
        "lines 必须包含")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[],"lines":[{"lineId":"l","productCode":"p","itemNumber":null,"lookupCode":"c","displayName":"n","quantity":1,"unitPriceCents":100,"basePriceSource":"catalog","syncProvenance":null,"kind":"sale","returnSourceKey":null,"originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"promotion","cents":10}}]}}""",
        "promotionIds 必须出现")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[],"lines":[],"unexpected":true}}""",
        "未知字段必须拒绝")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[],"lines":[{"lineId":"l","productCode":"p","itemNumber":null,"lookupCode":"c","displayName":"n","quantity":1,"unitPriceCents":-100,"basePriceSource":"catalog","syncProvenance":null,"kind":"sale","returnSourceKey":null,"originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"none"}}]}}""",
        "line.unitPriceCents 必须是 0 到")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[],"lines":[{"lineId":"l","productCode":"p","itemNumber":null,"lookupCode":"c","displayName":"n","quantity":1,"unitPriceCents":100,"basePriceSource":"open-item","syncProvenance":null,"kind":"sale","returnSourceKey":null,"originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"none"}}]}}""",
        "普通共享 sale 只允许 catalog 或 manual")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[],"lines":[{"lineId":"l","productCode":"p","itemNumber":null,"lookupCode":"c","displayName":"n","quantity":1,"unitPriceCents":100,"basePriceSource":"catalog","syncProvenance":null,"kind":"return","returnSourceKey":null,"originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"none"}}]}}""",
        "line.kind 必须是 sale")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[],"lines":[{"lineId":"l","productCode":"p","itemNumber":null,"lookupCode":"c","displayName":"n","quantity":1,"unitPriceCents":100,"basePriceSource":"catalog","syncProvenance":null,"kind":"sale","returnSourceKey":"RET-1","originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"none"}}]}}""",
        "line.returnSourceKey 必须是 null")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[],"lines":[{"lineId":"l","productCode":"p","itemNumber":null,"lookupCode":"c","displayName":"n","quantity":1,"unitPriceCents":100,"basePriceSource":"catalog","syncProvenance":{"referenceCode":"REF","priceSource":5},"kind":"sale","returnSourceKey":null,"originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"none"}}]}}""",
        "syncProvenance.priceSource 必须是 0..4")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[],"lines":[{"lineId":"l","productCode":"p","itemNumber":null,"lookupCode":"c","displayName":"n","quantity":1,"unitPriceCents":100,"basePriceSource":"catalog","syncProvenance":null,"kind":"sale","returnSourceKey":null,"originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"manual-amount","cents":101}}]}}""",
        "manual-amount 折扣不能超过行 gross")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[],"lines":[{"lineId":"l","productCode":"p","itemNumber":null,"lookupCode":"c","displayName":"n","quantity":1,"unitPriceCents":100,"basePriceSource":"catalog","syncProvenance":null,"kind":"sale","returnSourceKey":null,"originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"manual-percent","basisPoints":0}}]}}""",
        "manual-percent 折扣必须带 1..10000 basisPoints")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[],"lines":[{"lineId":"l","productCode":"p","itemNumber":null,"lookupCode":"c","displayName":"n","quantity":1,"unitPriceCents":100,"basePriceSource":"catalog","syncProvenance":null,"kind":"sale","returnSourceKey":null,"originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"promotion","cents":10,"promotionIds":["UNKNOWN"]}}]}}""",
        "promotionIds 必须引用冻结 promotions")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[{"id":"P1","name":"n","effectiveStartIso":"2026-07-01T00:00:00.000Z","effectiveEndIso":"2026-07-31T00:00:00.000Z","isExclusive":false,"priority":1,"applyQuantity":2,"fixedPriceCents":1000,"maxApplicationsPerOrder":null,"products":[{"productCode":"p","unitWeight":1}]}],"lines":[{"lineId":"l","productCode":"p","itemNumber":null,"lookupCode":"c","displayName":"n","quantity":1,"unitPriceCents":100,"basePriceSource":"catalog","syncProvenance":null,"kind":"sale","returnSourceKey":null,"originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"promotion","cents":10,"promotionIds":["P1","P1"]}}]}}""",
        "promotionIds 必须唯一")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[],"lines":[{"lineId":"l","productCode":"p","itemNumber":null,"lookupCode":"c","displayName":"n","quantity":1,"unitPriceCents":100,"basePriceSource":"catalog","syncProvenance":null,"kind":"sale","returnSourceKey":null,"originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"none"}},{"lineId":"l","productCode":"p2","itemNumber":null,"lookupCode":"c2","displayName":"n2","quantity":1,"unitPriceCents":100,"basePriceSource":"catalog","syncProvenance":null,"kind":"sale","returnSourceKey":null,"originalOrderGuid":null,"originalOrderDetailGuid":null,"discountState":{"mode":"none"}}]}}""",
        "line.lineId 必须唯一")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[{"id":"P1","name":"n","effectiveStartIso":"2026-07-01T00:00:00.000Z","effectiveEndIso":"2026-07-31T00:00:00.000Z","isExclusive":false,"priority":1,"applyQuantity":2,"fixedPriceCents":1000,"maxApplicationsPerOrder":null,"products":[{"productCode":"P-1","unitWeight":1}]},{"id":"P1","name":"n2","effectiveStartIso":"2026-07-01T00:00:00.000Z","effectiveEndIso":"2026-07-31T00:00:00.000Z","isExclusive":false,"priority":1,"applyQuantity":2,"fixedPriceCents":1000,"maxApplicationsPerOrder":null,"products":[{"productCode":"P-2","unitWeight":1}]}],"lines":[]}}""",
        "promotion.id 必须唯一")]
    [InlineData(
        """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[{"id":"p","name":"n","effectiveStartIso":"2026-07-01T00:00:00.000Z","effectiveEndIso":"2026-07-31T00:00:00.000Z","isExclusive":false,"priority":1,"applyQuantity":2,"fixedPriceCents":1000,"products":[]}],"lines":[]}}""",
        "maxApplicationsPerOrder 必须出现")]
    public void Deserialize_rejects_invalid_canonical(string json, string expectation)
    {
        var exception = Assert.Throws<SharedHeldOrderCanonicalValidationException>(() => Serializer.Deserialize(json));
        Assert.Contains(expectation, exception.Message);
    }

    [Fact]
    public void Deserialize_rejects_promotion_with_empty_products()
    {
        var json =
            """{"version":1,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-07-28T00:00:00.000Z","promotions":[{"id":"p","name":"n","effectiveStartIso":"2026-07-01T00:00:00.000Z","effectiveEndIso":"2026-07-31T00:00:00.000Z","isExclusive":false,"priority":1,"applyQuantity":2,"fixedPriceCents":1000,"maxApplicationsPerOrder":null,"products":[]}],"lines":[]}}""";

        var exception = Assert.Throws<SharedHeldOrderCanonicalValidationException>(() => Serializer.Deserialize(json));
        Assert.Contains("promotion.products 必须包含", exception.Message);
    }

    [Fact]
    public void Serialize_writes_null_max_applications_as_json_null()
    {
        var payload = new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                1,
                "sale",
                "2026-07-28T00:00:00.000Z",
                [
                    new SharedHeldOrderPromotionDefinition(
                        "PROMO-1",
                        "Unlimited",
                        "2026-07-01T00:00:00.000Z",
                        "2026-07-31T23:59:59.000Z",
                        false,
                        1,
                        2,
                        1000L,
                        null,
                        [new SharedHeldOrderPromotionProduct("P-1", 1m)])
                ],
                [
                    new SharedHeldOrderPricingLine(
                        "line-1",
                        "P-1",
                        null,
                        "CODE-1",
                        "Product 1",
                        1m,
                        100L,
                        "catalog",
                        null,
                        "sale",
                        null,
                        null,
                        null,
                        new SharedHeldOrderDiscountState("none"))
                ]));

        var json = Serializer.Serialize(payload);

        Assert.Contains("\"maxApplicationsPerOrder\":null", json);
        Assert.DoesNotContain("\"maxApplicationsPerOrder\":null,\"maxApplicationsPerOrder\"", json);
    }

    [Fact]
    public void Validate_accepts_max_cents_and_rejects_above_max()
    {
        var atLimit = new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                1,
                "sale",
                "2026-07-28T00:00:00.000Z",
                [],
                [
                    ValidLine() with
                    {
                        UnitPriceCents = SharedHeldOrderCanonicalConstants.MaxCents
                    }
                ]));
        var aboveLimit = new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                1,
                "sale",
                "2026-07-28T00:00:00.000Z",
                [],
                [
                    ValidLine() with
                    {
                        UnitPriceCents = SharedHeldOrderCanonicalConstants.MaxCents + 1
                    }
                ]));

        Assert.Contains("\"unitPriceCents\":1000000000000", Serializer.Serialize(atLimit));
        var exception = Assert.Throws<SharedHeldOrderCanonicalValidationException>(
            () => Serializer.Serialize(aboveLimit));
        Assert.Contains("line.unitPriceCents", exception.Message);
    }

    [Fact]
    public void Validate_rejects_collections_above_upper_limits()
    {
        var tooManyLines = new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                1,
                "sale",
                "2026-07-28T00:00:00.000Z",
                [],
                Enumerable.Range(1, SharedHeldOrderCanonicalConstants.MaxLineCount + 1)
                    .Select(i => ValidLine() with { LineId = "line-" + i })
                    .ToArray()));
        var tooManyPromotions = new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                1,
                "sale",
                "2026-07-28T00:00:00.000Z",
                Enumerable.Range(1, SharedHeldOrderCanonicalConstants.MaxPromotionCount + 1)
                    .Select(i => ValidPromotion() with { Id = "promo-" + i })
                    .ToArray(),
                [ValidLine()]));
        var tooManyProducts = new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                1,
                "sale",
                "2026-07-28T00:00:00.000Z",
                [
                    ValidPromotion() with
                    {
                        Products = Enumerable.Range(
                            1,
                            SharedHeldOrderCanonicalConstants.MaxPromotionProducts + 1)
                            .Select(i => new SharedHeldOrderPromotionProduct("P-" + i, 1m))
                            .ToArray()
                    }
                ],
                [ValidLine()]));

        AssertRejects(tooManyLines, "lines");
        AssertRejects(tooManyPromotions, "promotions");
        AssertRejects(tooManyProducts, "promotion.products");
    }

    [Fact]
    public void Validate_rejects_overlong_strings()
    {
        var overlongLineId = PayloadWithLine(ValidLine() with
        {
            LineId = new string('x', SharedHeldOrderCanonicalConstants.MaxCodeLength + 1)
        });
        var overlongName = PayloadWithLine(ValidLine() with
        {
            DisplayName = new string('x', SharedHeldOrderCanonicalConstants.MaxNameLength + 1)
        });
        var overlongReference = PayloadWithLine(ValidLine() with
        {
            SyncProvenance = new SharedHeldOrderLineSyncProvenance(
                new string('x', SharedHeldOrderCanonicalConstants.MaxReferenceLength + 1),
                0)
        });
        var overlongPromotionId = PayloadWithPromotion(ValidPromotion() with
        {
            Id = new string('x', SharedHeldOrderCanonicalConstants.MaxCodeLength + 1)
        });
        var overlongPromotionName = PayloadWithPromotion(ValidPromotion() with
        {
            Name = new string('x', SharedHeldOrderCanonicalConstants.MaxNameLength + 1)
        });
        var overlongProductCode = PayloadWithPromotion(ValidPromotion() with
        {
            Products = [new SharedHeldOrderPromotionProduct(
                new string('x', SharedHeldOrderCanonicalConstants.MaxCodeLength + 1),
                1m)]
        });

        AssertRejects(overlongLineId, "line.lineId");
        AssertRejects(overlongName, "line.displayName");
        AssertRejects(overlongReference, "syncProvenance.referenceCode");
        AssertRejects(overlongPromotionId, "promotion.id");
        AssertRejects(overlongPromotionName, "promotion.name");
        AssertRejects(overlongProductCode, "promotion.product.productCode");
    }

    [Fact]
    public void Validate_rejects_promotion_priority_above_upper_limit()
    {
        var overLimit = PayloadWithPromotion(ValidPromotion() with
        {
            Priority = (int)SharedHeldOrderCanonicalConstants.MaxQuantity + 1
        });
        var atLimit = PayloadWithPromotion(ValidPromotion() with
        {
            Priority = (int)SharedHeldOrderCanonicalConstants.MaxQuantity
        });

        AssertRejects(overLimit, "promotion.priority");
        Serializer.Serialize(atLimit);
    }

    [Fact]
    public void Validate_uses_rounded_gross_for_discount_upper_bound()
    {
        // 0.5 * 501 = 250.5 -> AwayFromZero 取整为 251。
        var atManualGross = PayloadWithLine(ValidLine() with
        {
            Quantity = 0.5m,
            UnitPriceCents = 501,
            DiscountState = new SharedHeldOrderDiscountState(
                "manual-amount",
                Cents: 251)
        });
        var aboveManualGross = PayloadWithLine(ValidLine() with
        {
            Quantity = 0.5m,
            UnitPriceCents = 501,
            DiscountState = new SharedHeldOrderDiscountState(
                "manual-amount",
                Cents: 252)
        });
        var abovePromotionGross = PayloadWithPromotionAndLine(
            ValidPromotion(),
            ValidLine() with
            {
                Quantity = 0.5m,
                UnitPriceCents = 501,
                DiscountState = new SharedHeldOrderDiscountState(
                    "promotion",
                    Cents: 252,
                    PromotionIds: ["PROMO-1"])
            });

        Serializer.Serialize(atManualGross);
        AssertRejects(aboveManualGross, "gross");
        AssertRejects(abovePromotionGross, "gross");
    }

    [Fact]
    public void Validate_accepts_single_line_rounded_gross_at_max_safe_integer()
    {
        // 69_431 * 129_728_784_761 = 9_007_199_254_740_991 = MaxTotalCents。
        var payload = PayloadWithLine(ValidLine() with
        {
            Quantity = 69_431m,
            UnitPriceCents = 129_728_784_761L
        });

        var json = Serializer.Serialize(payload);

        Assert.Contains("\"unitPriceCents\":129728784761", json);
    }

    [Fact]
    public void Validate_rejects_single_line_rounded_gross_above_max_safe_integer()
    {
        var payload = PayloadWithLine(ValidLine() with
        {
            Quantity = 69_432m,
            UnitPriceCents = 129_728_784_761L
        });

        AssertRejects(payload, "rounded gross");
    }

    [Fact]
    public void Validate_rejects_gross_total_above_max_safe_integer_when_each_line_is_safe()
    {
        // 每行 65_536 * 68_719_476_736 = 2^52（各自安全），
        // 两行合计 = 2^53 > MaxTotalCents。
        var safeLine = ValidLine() with
        {
            Quantity = 65_536m,
            UnitPriceCents = 68_719_476_736L
        };
        var payload = new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                1,
                "sale",
                "2026-07-28T00:00:00.000Z",
                [],
                [
                    safeLine with { LineId = "line-boundary-1" },
                    safeLine with { LineId = "line-boundary-2" }
                ]));

        AssertRejects(payload, "合计不能超过");
    }

    [Fact]
    public void Validate_rejects_discount_promotion_ids_not_referencing_frozen_promotions()
    {
        var payload = PayloadWithPromotionAndLine(
            ValidPromotion(),
            ValidLine() with
            {
                DiscountState = new SharedHeldOrderDiscountState(
                    "promotion",
                    Cents: 10,
                    PromotionIds: ["UNKNOWN"])
            });

        AssertRejects(payload, "必须引用冻结 promotions");
    }

    private static SharedHeldOrderCanonicalPayload PayloadWithLine(
        SharedHeldOrderPricingLine line)
    {
        return new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                1,
                "sale",
                "2026-07-28T00:00:00.000Z",
                [],
                [line]));
    }

    private static SharedHeldOrderCanonicalPayload PayloadWithPromotion(
        SharedHeldOrderPromotionDefinition promotion)
    {
        return new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                1,
                "sale",
                "2026-07-28T00:00:00.000Z",
                [promotion],
                [ValidLine()]));
    }

    private static SharedHeldOrderCanonicalPayload PayloadWithPromotionAndLine(
        SharedHeldOrderPromotionDefinition promotion,
        SharedHeldOrderPricingLine line)
    {
        return new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                1,
                "sale",
                "2026-07-28T00:00:00.000Z",
                [promotion],
                [line]));
    }

    private static SharedHeldOrderPricingLine ValidLine() => new(
        "line-1",
        "P-1",
        null,
        "CODE-1",
        "Product 1",
        1m,
        100L,
        "catalog",
        null,
        "sale",
        null,
        null,
        null,
        new SharedHeldOrderDiscountState("none"));

    private static SharedHeldOrderPromotionDefinition ValidPromotion() => new(
        "PROMO-1",
        "Buy 2 save 10",
        "2026-07-01T00:00:00.000Z",
        "2026-07-31T23:59:59.000Z",
        true,
        100,
        2,
        2000L,
        1,
        [new SharedHeldOrderPromotionProduct("P-1", 1m)]);

    private static void AssertRejects(
        SharedHeldOrderCanonicalPayload payload,
        string expectedFragment)
    {
        var exception = Assert.Throws<SharedHeldOrderCanonicalValidationException>(
            () => Serializer.Serialize(payload));
        Assert.Contains(expectedFragment, exception.Message);
    }
}
