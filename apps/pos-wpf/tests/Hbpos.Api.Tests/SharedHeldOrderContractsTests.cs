using System.Text.Json;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.HeldOrders;
using Hbpos.Contracts.Orders;

namespace Hbpos.Api.Tests;

public sealed class SharedHeldOrderContractsTests
{
    [Fact]
    public void HeldOrderSourceDto_wire_exposes_only_holdGuid_claimGuid_sourceKind()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(
            new HeldOrderSourceDto(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                HeldOrderSourceKind.RemoteClaim),
            options);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(3, root.EnumerateObject().Count());
        Assert.True(root.TryGetProperty("holdGuid", out _));
        Assert.True(root.TryGetProperty("claimGuid", out _));
        Assert.True(root.TryGetProperty("sourceKind", out _));
        Assert.False(root.TryGetProperty("kind", out _));
    }

    [Fact]
    public void HeldOrderSourceDto_wire_roundtrip_keeps_explicit_source_kind()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var source = new HeldOrderSourceDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            HeldOrderSourceKind.RemoteClaim);

        var roundtrip = JsonSerializer.Deserialize<HeldOrderSourceDto>(
            JsonSerializer.Serialize(source, options),
            options)!;

        Assert.Equal(source, roundtrip);
        Assert.Equal(HeldOrderSourceKind.RemoteClaim, roundtrip.Kind);
    }

    [Fact]
    public void Valid_frozen_sale_cart_passes_canonical_validation()
    {
        var cart = ValidCart();

        var validated = SharedSaleCartV1Validator.Validate(cart);

        Assert.Same(cart, validated);
        Assert.Empty(SharedSaleCartV1Validator.ValidateAll(cart));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public void Cart_rejects_version_other_than_one(int version)
    {
        var cart = ValidCart() with { Version = version };

        AssertInvalid("version", cart);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Pricing_state_rejects_non_positive_revision(int revision)
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with { Revision = revision }
        };

        AssertInvalid("revision", cart);
    }

    [Fact]
    public void Pricing_state_rejects_revision_above_upper_limit()
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Revision = SharedSaleCartV1Constants.MaxQuantity + 1
            }
        };

        AssertInvalid("revision", cart);
    }

    [Theory]
    [InlineData("return")]
    [InlineData("open-item")]
    [InlineData("sale ")]
    [InlineData("")]
    public void Pricing_state_accepts_only_sale_mode(string mode)
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with { Mode = mode }
        };

        AssertInvalid("mode", cart);
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("2026-08-10T02:00:00+10:00")]
    [InlineData("")]
    public void Pricing_state_requires_utc_iso_as_of_timestamp(string asOfIso)
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with { AsOfIso = asOfIso }
        };

        AssertInvalid("asOfIso", cart);
    }

    [Fact]
    public void Cart_rejects_empty_line_list()
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with { Lines = [] }
        };

        AssertInvalid("lines", cart);
    }

    [Fact]
    public void Cart_rejects_duplicate_line_ids()
    {
        var line = ValidLine();
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines = [line, line with { LineId = "L1" }]
            }
        };

        AssertInvalid("lineId", cart);
    }

    [Fact]
    public void Cart_rejects_collections_above_upper_limits()
    {
        var tooManyLines = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines = Enumerable.Range(1, SharedSaleCartV1Constants.MaxLineCount + 1)
                    .Select(i => ValidLine() with { LineId = "L" + i })
                    .ToArray()
            }
        };
        var tooManyPromotions = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Promotions = Enumerable.Range(1, SharedSaleCartV1Constants.MaxPromotionCount + 1)
                    .Select(i => ValidPromotion() with { Id = "P" + i })
                    .ToArray()
            }
        };
        var tooManyProducts = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Promotions =
                [
                    ValidPromotion() with
                    {
                        Products = Enumerable.Range(
                            1,
                            SharedSaleCartV1Constants.MaxPromotionProducts + 1)
                            .Select(i => new SharedPromotionProductV1("SKU-" + i, 1m))
                            .ToArray()
                    }
                ]
            }
        };

        AssertInvalid("lines", tooManyLines);
        AssertInvalid("promotions", tooManyPromotions);
        AssertInvalid("products", tooManyProducts);
    }

    [Theory]
    [InlineData("return")]
    [InlineData("open-item")]
    [InlineData("sale-return")]
    public void Line_rejects_non_sale_kind(string kind)
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines = [ValidLine() with { Kind = kind }]
            }
        };

        AssertInvalid("kind", cart);
    }

    [Theory]
    [InlineData("promotion")]
    [InlineData("open-item")]
    [InlineData("catalog ")]
    [InlineData("")]
    [InlineData("manual-special")]
    public void Line_rejects_non_catalog_or_manual_base_price_source(string basePriceSource)
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines = [ValidLine() with { BasePriceSource = basePriceSource }]
            }
        };

        AssertInvalid("basePriceSource", cart);
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("manual")]
    public void Line_accepts_catalog_and_manual_base_price_source(string basePriceSource)
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines = [ValidLine() with { BasePriceSource = basePriceSource }]
            }
        };

        Assert.Empty(SharedSaleCartV1Validator.ValidateAll(cart));
    }

    [Fact]
    public void Line_rejects_non_null_return_and_original_order_fields()
    {
        var withReturnSource = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines = [ValidLine() with { ReturnSourceKey = "RET-1" }]
            }
        };
        var withOriginalOrder = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines = [ValidLine() with { OriginalOrderGuid = Guid.NewGuid() }]
            }
        };
        var withOriginalDetail = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines = [ValidLine() with { OriginalOrderDetailGuid = Guid.NewGuid() }]
            }
        };

        AssertInvalid("returnSourceKey", withReturnSource);
        AssertInvalid("originalOrderGuid", withOriginalOrder);
        AssertInvalid("originalOrderDetailGuid", withOriginalDetail);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Line_rejects_non_positive_quantity(decimal quantity)
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines = [ValidLine() with { Quantity = quantity }]
            }
        };

        AssertInvalid("quantity", cart);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(SharedSaleCartV1Constants.MaxCents + 1)]
    public void Line_rejects_out_of_bounds_unit_price_cents(long cents)
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines = [ValidLine() with { UnitPriceCents = cents }]
            }
        };

        AssertInvalid("unitPriceCents", cart);
    }

    [Fact]
    public void Line_accepts_max_cents()
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines = [ValidLine() with { UnitPriceCents = SharedSaleCartV1Constants.MaxCents }]
            }
        };

        Assert.Empty(SharedSaleCartV1Validator.ValidateAll(cart));
    }

    [Fact]
    public void Line_accepts_rounded_gross_at_max_safe_integer()
    {
        // 69_431 * 129_728_784_761 = 9_007_199_254_740_991 = MaxTotalCents。
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines =
                [
                    ValidLine() with
                    {
                        Quantity = 69_431m,
                        UnitPriceCents = 129_728_784_761L
                    }
                ]
            }
        };

        Assert.Empty(SharedSaleCartV1Validator.ValidateAll(cart));
    }

    [Fact]
    public void Line_rejects_rounded_gross_above_max_safe_integer()
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines =
                [
                    ValidLine() with
                    {
                        Quantity = 69_432m,
                        UnitPriceCents = 129_728_784_761L
                    }
                ]
            }
        };

        AssertInvalid("rounded gross", cart);
    }

    [Fact]
    public void Cart_rejects_gross_total_above_max_safe_integer_when_each_line_is_safe()
    {
        // 每行 65_536 * 68_719_476_736 = 2^52（各自安全），
        // 两行合计 = 2^53 > MaxTotalCents。
        var safeLine = ValidLine() with
        {
            Quantity = 65_536m,
            UnitPriceCents = 68_719_476_736L
        };
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines =
                [
                    safeLine with { LineId = "L-boundary-1" },
                    safeLine with { LineId = "L-boundary-2" }
                ]
            }
        };

        AssertInvalid("gross total", cart);
    }

    [Fact]
    public void Line_rejects_overlong_codes_names_and_reference()
    {
        var overlongLineId = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines = [ValidLine() with { LineId = new string('x', SharedSaleCartV1Constants.MaxCodeLength + 1) }]
            }
        };
        var overlongName = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines = [ValidLine() with { DisplayName = new string('x', SharedSaleCartV1Constants.MaxNameLength + 1) }]
            }
        };
        var overlongReference = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines =
                [
                    ValidLine() with
                    {
                        SyncProvenance = new SharedLineSyncProvenanceV1(
                            new string('x', SharedSaleCartV1Constants.MaxReferenceLength + 1),
                            PriceSourceKind.StoreRetailPrice)
                    }
                ]
            }
        };

        AssertInvalid("lineId", overlongLineId);
        AssertInvalid("displayName", overlongName);
        AssertInvalid("referenceCode", overlongReference);
    }

    [Fact]
    public void Line_rejects_blank_codes_and_display_name()
    {
        var blankProduct = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines = [ValidLine() with { ProductCode = " " }]
            }
        };
        var blankLookup = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines = [ValidLine() with { LookupCode = string.Empty }]
            }
        };
        var blankName = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines = [ValidLine() with { DisplayName = string.Empty }]
            }
        };

        AssertInvalid("productCode", blankProduct);
        AssertInvalid("lookupCode", blankLookup);
        AssertInvalid("displayName", blankName);
    }

    [Theory]
    [InlineData("none", null, null, null)]
    [InlineData("manual-amount", 0L, null, null)]
    [InlineData("manual-percent", null, 10000, null)]
    [InlineData("promotion", 500L, null, new[] { "P1" })]
    public void Valid_discount_modes_pass(string mode, long? cents, int? basisPoints, string[]? promotionIds)
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines =
                [
                    ValidLine() with
                    {
                        DiscountState = new SharedLineDiscountStateV1(
                            mode,
                            cents,
                            basisPoints,
                            promotionIds)
                    }
                ]
            }
        };

        Assert.Empty(SharedSaleCartV1Validator.ValidateAll(cart));
    }

    [Fact]
    public void Discount_state_wire_omits_fields_that_do_not_belong_to_the_active_mode()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var cases = new[]
        {
            (State: new SharedLineDiscountStateV1("none"), Fields: new[] { "mode" }),
            (State: new SharedLineDiscountStateV1("manual-amount", Cents: 25), Fields: new[] { "mode", "cents" }),
            (State: new SharedLineDiscountStateV1("manual-percent", BasisPoints: 500), Fields: new[] { "mode", "basisPoints" }),
            (State: new SharedLineDiscountStateV1("promotion", Cents: 10, PromotionIds: ["P1"]), Fields: new[] { "mode", "cents", "promotionIds" })
        };

        foreach (var testCase in cases)
        {
            using var document = JsonDocument.Parse(
                JsonSerializer.Serialize(testCase.State, options));
            var actualFields = document.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                testCase.Fields.Order(StringComparer.Ordinal),
                actualFields);
        }
    }

    [Theory]
    [InlineData("none", 1L, null, null, "cents")]
    [InlineData("manual-amount", null, null, null, "cents")]
    [InlineData("manual-amount", -1L, null, null, "cents")]
    [InlineData("manual-percent", null, 0, null, "basisPoints")]
    [InlineData("manual-percent", null, 10001, null, "basisPoints")]
    [InlineData("manual-percent", null, -100, null, "basisPoints")]
    [InlineData("promotion", 1L, null, null, "promotionIds")]
    [InlineData("promotion", 1L, null, new[] { "P1", "P1" }, "promotionIds")]
    [InlineData("promotion", -1L, null, new[] { "P1" }, "cents")]
    [InlineData("sale-wide", null, null, null, "mode")]
    public void Invalid_discount_states_are_rejected(
        string mode,
        long? cents,
        int? basisPoints,
        string[]? promotionIds,
        string expectedFragment)
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines =
                [
                    ValidLine() with
                    {
                        DiscountState = new SharedLineDiscountStateV1(
                            mode,
                            cents,
                            basisPoints,
                            promotionIds)
                    }
                ]
            }
        };

        AssertInvalid(expectedFragment, cart);
    }

    [Fact]
    public void Promotions_reject_duplicate_ids_and_empty_products()
    {
        var promotion = ValidPromotion();
        var duplicate = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Promotions = [promotion, promotion with { Id = "P1" }]
            }
        };
        var emptyProducts = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Promotions = [promotion with { Products = [] }]
            }
        };

        AssertInvalid("promotion id", duplicate);
        AssertInvalid("products", emptyProducts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SharedSaleCartV1Constants.MaxQuantity + 1)]
    public void Promotion_rejects_out_of_bounds_apply_quantity(int applyQuantity)
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Promotions = [ValidPromotion() with { ApplyQuantity = applyQuantity }]
            }
        };

        AssertInvalid("applyQuantity", cart);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(SharedSaleCartV1Constants.MaxCents + 1)]
    public void Promotion_rejects_out_of_bounds_fixed_price_cents(long cents)
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Promotions = [ValidPromotion() with { FixedPriceCents = cents }]
            }
        };

        AssertInvalid("fixedPriceCents", cart);
    }

    [Fact]
    public void Promotion_rejects_negative_priority_and_bad_product_weight()
    {
        var negativePriority = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Promotions = [ValidPromotion() with { Priority = -1 }]
            }
        };
        var negativeWeight = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Promotions =
                [
                    ValidPromotion() with
                    {
                        Products = [new SharedPromotionProductV1("SKU-1", -0.1m)]
                    }
                ]
            }
        };

        AssertInvalid("priority", negativePriority);
        AssertInvalid("unitWeight", negativeWeight);
    }

    [Fact]
    public void Promotion_rejects_priority_above_upper_limit()
    {
        var overLimit = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Promotions = [ValidPromotion() with { Priority = SharedSaleCartV1Constants.MaxQuantity + 1 }]
            }
        };
        var atLimit = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Promotions = [ValidPromotion() with { Priority = SharedSaleCartV1Constants.MaxQuantity }]
            }
        };

        AssertInvalid("priority", overLimit);
        Assert.Empty(SharedSaleCartV1Validator.ValidateAll(atLimit));
    }

    [Fact]
    public void Promotion_rejects_overlong_id_name_and_product_code()
    {
        var overlongId = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Promotions = [ValidPromotion() with { Id = new string('x', SharedSaleCartV1Constants.MaxCodeLength + 1) }]
            }
        };
        var overlongName = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Promotions = [ValidPromotion() with { Name = new string('x', SharedSaleCartV1Constants.MaxNameLength + 1) }]
            }
        };
        var overlongProductCode = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Promotions =
                [
                    ValidPromotion() with
                    {
                        Products =
                        [
                            new SharedPromotionProductV1(
                                new string('x', SharedSaleCartV1Constants.MaxCodeLength + 1),
                                1m)
                        ]
                    }
                ]
            }
        };

        AssertInvalid("promotion.id", overlongId);
        AssertInvalid("promotion.name", overlongName);
        AssertInvalid("productCode", overlongProductCode);
    }

    [Fact]
    public void Discount_rejects_cents_above_rounded_line_gross()
    {
        // 0.5 * 501 = 250.5 -> AwayFromZero 取整为 251。
        var atRoundedGross = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines =
                [
                    ValidLine() with
                    {
                        Quantity = 0.5m,
                        UnitPriceCents = 501,
                        DiscountState = new SharedLineDiscountStateV1(
                            "manual-amount",
                            Cents: 251)
                    }
                ]
            }
        };
        var aboveManual = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines =
                [
                    ValidLine() with
                    {
                        Quantity = 0.5m,
                        UnitPriceCents = 501,
                        DiscountState = new SharedLineDiscountStateV1(
                            "manual-amount",
                            Cents: 252)
                    }
                ]
            }
        };
        var abovePromotion = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines =
                [
                    ValidLine() with
                    {
                        Quantity = 0.5m,
                        UnitPriceCents = 501,
                        DiscountState = new SharedLineDiscountStateV1(
                            "promotion",
                            Cents: 252,
                            PromotionIds: ["P1"])
                    }
                ]
            }
        };

        Assert.Empty(SharedSaleCartV1Validator.ValidateAll(atRoundedGross));
        AssertInvalid("gross", aboveManual);
        AssertInvalid("gross", abovePromotion);
    }

    [Fact]
    public void Discount_promotion_ids_must_reference_frozen_promotions()
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Lines =
                [
                    ValidLine() with
                    {
                        DiscountState = new SharedLineDiscountStateV1(
                            "promotion",
                            Cents: 10,
                            PromotionIds: ["UNKNOWN"])
                    }
                ]
            }
        };

        AssertInvalid("reference frozen promotions", cart);
    }

    [Fact]
    public void Promotion_rejects_effective_window_when_end_before_start()
    {
        var cart = ValidCart() with
        {
            PricingState = ValidCart().PricingState with
            {
                Promotions =
                [
                    ValidPromotion() with
                    {
                        EffectiveStartIso = "2026-08-31T00:00:00Z",
                        EffectiveEndIso = "2026-08-01T00:00:00Z"
                    }
                ]
            }
        };

        AssertInvalid("effectiveEndIso", cart);
    }

    private static SharedSaleCartV1 ValidCart() => new(
        Version: 1,
        new SharedPricingStateV1(
            Revision: 1,
            Mode: SharedSaleCartV1Constants.PricingModeSale,
            AsOfIso: "2026-08-10T02:00:00Z",
            Promotions: [ValidPromotion()],
            Lines: [ValidLine()]));

    private static SharedPromotionV1 ValidPromotion() => new(
        Id: "P1",
        Name: "特价促销",
        EffectiveStartIso: "2026-08-01T00:00:00Z",
        EffectiveEndIso: "2026-08-31T00:00:00Z",
        IsExclusive: true,
        Priority: 10,
        ApplyQuantity: 2,
        FixedPriceCents: 1000,
        MaxApplicationsPerOrder: 1,
        Products: [new SharedPromotionProductV1("SKU-1", 0.25m)]);

    private static SharedSaleLineV1 ValidLine() => new(
        LineId: "L1",
        ProductCode: "SKU-1",
        ItemNumber: "ITEM-1",
        LookupCode: "BAR-1",
        DisplayName: "测试商品",
        Quantity: 2m,
        UnitPriceCents: 1500,
        BasePriceSource: SharedSaleCartV1Constants.PriceSourceCatalog,
        SyncProvenance: new SharedLineSyncProvenanceV1("REF-1", PriceSourceKind.StoreRetailPrice),
        Kind: SharedSaleCartV1Constants.LineKindSale,
        ReturnSourceKey: null,
        OriginalOrderGuid: null,
        OriginalOrderDetailGuid: null,
        DiscountState: new SharedLineDiscountStateV1(Mode: "none"));

    private static void AssertInvalid(string expectedFragment, SharedSaleCartV1 cart)
    {
        var errors = SharedSaleCartV1Validator.ValidateAll(cart);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, error => error.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
        Assert.Throws<SharedSaleCartValidationException>(() => SharedSaleCartV1Validator.Validate(cart));
    }
}
