using System.Text.Json;
using Hbpos.Contracts.HeldOrders;

namespace Hbpos.Api.Tests;

/// <summary>
/// SharedSaleCartV2 / capabilities / version-dispatch 的契约测试。
/// V1 冻结契约仍由 SharedHeldOrderContractsTests / FixtureContractTests 覆盖，此处不重写 V1。
/// </summary>
public sealed class SharedHeldOrderContractsV2Tests
{
    [Fact]
    public void Valid_v2_cart_with_catalog_baseline_passes_validation()
    {
        var cart = ValidV2Cart();

        var validated = SharedSaleCartV2Validator.Validate(cart);

        Assert.Same(cart, validated);
        Assert.Empty(SharedSaleCartV2Validator.ValidateAll(cart));
    }

    [Fact]
    public void V2_rejects_catalog_baseline_with_promotion_discount()
    {
        var cart = ValidV2Cart();
        var line = cart.PricingState.Lines[0] with
        {
            DiscountState = new SharedLineDiscountStateV1(
                SharedSaleCartV1Constants.DiscountModePromotion,
                Cents: 100,
                PromotionIds: ["promo-1"])
        };
        var invalid = cart with
        {
            PricingState = cart.PricingState with { Lines = [line] }
        };

        Assert.NotEmpty(SharedSaleCartV2Validator.ValidateAll(invalid));
    }

    [Fact]
    public void V2_null_line_is_reported_as_validation_error_instead_of_throwing_null_reference()
    {
        var cart = ValidV2Cart();
        var invalid = cart with
        {
            PricingState = cart.PricingState with { Lines = [null!] }
        };

        var errors = SharedSaleCartV2Validator.ValidateAll(invalid);

        Assert.Contains("line is required", errors);
        Assert.Throws<SharedSaleCartValidationException>(() =>
            SharedSaleCartV2Validator.Validate(invalid));
    }

    [Fact]
    public void V2_null_lines_collection_is_reported_as_validation_error()
    {
        var cart = ValidV2Cart();
        var invalid = cart with
        {
            PricingState = cart.PricingState with { Lines = null! }
        };

        var errors = SharedSaleCartV2Validator.ValidateAll(invalid);

        Assert.Contains(errors, error => error.Contains("lines", StringComparison.Ordinal));
        Assert.Throws<SharedSaleCartValidationException>(() =>
            SharedSaleCartV2Validator.Validate(invalid));
    }

    [Fact]
    public void V2_accepts_manual_discount_over_catalog_baseline()
    {
        var cart = ValidV2Cart();

        var errors = SharedSaleCartV2Validator.ValidateAll(cart);

        Assert.Empty(errors);
        Assert.Equal(1500, cart.PricingState.Lines[0].CatalogDiscountBasisPoints);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10001)]
    public void V2_rejects_catalog_basis_points_out_of_range(int basisPoints)
    {
        var cart = ValidV2Cart();
        var invalid = cart with
        {
            PricingState = cart.PricingState with
            {
                Lines = [cart.PricingState.Lines[0] with { CatalogDiscountBasisPoints = basisPoints }]
            }
        };

        Assert.NotEmpty(SharedSaleCartV2Validator.ValidateAll(invalid));
    }

    [Fact]
    public void Capabilities_missing_new_fields_falls_back_to_legacy_v1_only()
    {
        var response = new SharedHeldOrderCapabilitiesResponse(
            Enabled: true,
            PayloadVersion: 1,
            PreparedTtlSeconds: 120,
            ForceReleaseSupported: true);

        Assert.Equal(1, response.PayloadVersion);
        Assert.Equal([1], response.SupportedPayloadVersions);
        Assert.Equal(1, response.PreferredPayloadVersion);
    }

    [Fact]
    public void Publish_request_carries_versioned_cart_base()
    {
        var v1 = new SharedHeldOrderPublishRequest(
            Guid.NewGuid(), "S1", "D1", ValidV2Cart().AsV1(), "k1");

        Assert.IsType<SharedSaleCartV1>(v1.Cart);
        Assert.Equal(1, ((SharedSaleCartV1)v1.Cart).Version);

        var v2 = new SharedHeldOrderPublishRequest(
            Guid.NewGuid(), "S1", "D1", ValidV2Cart(), "k1");

        Assert.IsType<SharedSaleCartV2>(v2.Cart);
        Assert.Equal(2, ((SharedSaleCartV2)v2.Cart).Version);
    }

    [Fact]
    public void Json_converter_rejects_v2_line_missing_catalog_basis_points()
    {
        var options = WebJsonOptions();
        var json = """
            {"holdGuid":"11111111-1111-1111-1111-111111111111","storeCode":"S1","deviceCode":"D1",
             "idempotencyKey":"k1",
             "cart":{"version":2,"pricingState":{"revision":1,"mode":"sale","asOfIso":"2026-08-10T02:00:00Z",
             "promotions":[],"lines":[{"lineId":"L1","productCode":"SKU-1","itemNumber":null,"lookupCode":"BAR-1",
             "displayName":"x","quantity":2,"unitPriceCents":1500,"basePriceSource":"catalog",
             "syncProvenance":null,"kind":"sale","returnSourceKey":null,"originalOrderGuid":null,
             "originalOrderDetailGuid":null,"discountState":{"mode":"none"}}]}}}
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SharedHeldOrderPublishRequest>(json, options));
    }

    [Fact]
    public void Publish_prepare_recovery_payloads_roundtrip_v1_and_v2_direct_wire()
    {
        var options = WebJsonOptions();

        var v1Publish = new SharedHeldOrderPublishRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "S1",
            "D1",
            SharedHeldOrderServiceTestSupport.ValidCart(),
            "k1");
        var v1Json = JsonSerializer.Serialize(v1Publish, options);
        Assert.Contains("\"cart\":", v1Json, StringComparison.Ordinal);
        var v1Back = JsonSerializer.Deserialize<SharedHeldOrderPublishRequest>(v1Json, options)!;
        Assert.IsType<SharedSaleCartV1>(v1Back.Cart);

        var v2Publish = v1Publish with
        {
            Cart = SharedHeldOrderServiceTestSupport.ValidV2Cart(),
            IdempotencyKey = "k2"
        };
        var v2Json = JsonSerializer.Serialize(v2Publish, options);
        var v2Back = JsonSerializer.Deserialize<SharedHeldOrderPublishRequest>(v2Json, options)!;
        Assert.IsType<SharedSaleCartV2>(v2Back.Cart);
        Assert.Equal(500, ((SharedSaleCartV2)v2Back.Cart).PricingState.Lines[0].CatalogDiscountBasisPoints);

        var prepare = new SharedHeldOrderClaimPrepareResponse(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            SharedHeldOrderClaimStatus.Prepared,
            SharedHeldOrderServiceTestSupport.ValidV2Cart(),
            "POS-01",
            "C01",
            "持单收银员",
            DateTimeOffset.Parse("2026-08-10T02:00:00Z"),
            null,
            1);
        var prepareBack = JsonSerializer.Deserialize<SharedHeldOrderClaimPrepareResponse>(
            JsonSerializer.Serialize(prepare, options), options)!;
        Assert.IsType<SharedSaleCartV2>(prepareBack.Payload);

        var recovery = new SharedHeldOrderRecoveryClaimDto(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            SharedHeldOrderClaimStatus.Prepared,
            "S1",
            "POS-01",
            "C01",
            "持单收银员",
            SharedHeldOrderServiceTestSupport.ValidCart(),
            DateTimeOffset.Parse("2026-08-10T02:00:00Z"),
            DateTimeOffset.Parse("2026-08-10T02:00:00Z"),
            null,
            null,
            1);
        var recoveryBack = JsonSerializer.Deserialize<SharedHeldOrderRecoveryClaimDto>(
            JsonSerializer.Serialize(recovery, options), options)!;
        Assert.IsType<SharedSaleCartV1>(recoveryBack.Payload);
    }

    private static JsonSerializerOptions WebJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new SharedSaleCartPayloadJsonConverter());
        return options;
    }

    private static SharedSaleCartV2 ValidV2Cart() => new(
        2,
        new SharedPricingStateV2(
            7,
            SharedSaleCartV1Constants.PricingModeSale,
            "2026-07-28T08:00:00.000Z",
            [
                new SharedPromotionV1(
                    "promo-1",
                    "Three for five",
                    "2026-07-01T00:00:00.000Z",
                    "2026-08-01T00:00:00.000Z",
                    IsExclusive: false,
                    Priority: 1,
                    ApplyQuantity: 1,
                    FixedPriceCents: 500,
                    MaxApplicationsPerOrder: null,
                    Products: [new SharedPromotionProductV1("P-PROMO", 1m)])
            ],
            [
                new SharedSaleLineV2(
                    "line-1",
                    "P-1",
                    "100",
                    "100",
                    "Item one",
                    2m,
                    501L,
                    SharedSaleCartV1Constants.PriceSourceManual,
                    new SharedLineSyncProvenanceV1("REF-1", Hbpos.Contracts.Catalog.PriceSourceKind.StoreRetailPrice),
                    SharedSaleCartV1Constants.LineKindSale,
                    ReturnSourceKey: null,
                    OriginalOrderGuid: null,
                    OriginalOrderDetailGuid: null,
                    DiscountState: new SharedLineDiscountStateV1(
                        SharedSaleCartV1Constants.DiscountModeManualAmount,
                        Cents: 102),
                    CatalogDiscountBasisPoints: 1500)
            ]));
}

public static class SharedHeldOrderContractsV2TestExtensions
{
    public static SharedSaleCartV1 AsV1(this SharedSaleCartV2 cart) => new(
        SharedSaleCartV1Constants.PayloadVersion,
        new SharedPricingStateV1(
            cart.PricingState.Revision,
            cart.PricingState.Mode,
            cart.PricingState.AsOfIso,
            cart.PricingState.Promotions,
            cart.PricingState.Lines.Select(line => new SharedSaleLineV1(
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
                line.DiscountState)).ToArray()));
}
