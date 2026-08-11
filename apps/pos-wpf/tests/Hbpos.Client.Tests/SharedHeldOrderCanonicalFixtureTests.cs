using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

/// <summary>
/// 跨端 frozen wire fixture lane（WPF strict client serializer 侧）：
/// 与 iPad shared-sale-cart-v1-fixture.test.ts 共用 test-fixtures/shared-held-orders/ 同一批 JSON。
/// 字节稳定判定：Deserialize(fixture) 后 Serialize 必须逐字节还原 fixture。
/// fixture 文件带 POSIX 结尾换行，读取后统一 TrimEnd，与 iPad 侧 trimEnd 一致。
/// </summary>
public sealed class SharedHeldOrderCanonicalFixtureTests
{
    private static readonly ISharedHeldOrderCanonicalSerializer Serializer =
        new SharedHeldOrderCanonicalJsonSerializer();

    private static string ReadFixture(string name)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "test-fixtures",
            "shared-held-orders",
            name);
        return File.ReadAllText(path).TrimEnd('\r', '\n');
    }

    [Fact]
    public void Canonical_fixture_roundtrip_is_byte_stable()
    {
        var fixture = ReadFixture("shared-sale-cart-v1.canonical.json");

        var json = Serializer.Serialize(Serializer.Deserialize(fixture));

        Assert.Equal(fixture, json);
    }

    [Fact]
    public void Canonical_fixture_decimal_away_from_zero_manual_percent_and_frozen_promotions_are_preserved()
    {
        var fixture = ReadFixture("shared-sale-cart-v1.canonical.json");

        var payload = Serializer.Deserialize(fixture);
        var lines = payload.PricingState.Lines;

        // decimal 数量 + AwayFromZero：1.5 * 1003 = 1504.5 -> gross 1505，
        // manual-amount 1505 能通过恰好证明 strict validator 用 half-away-from-zero 判定折扣上限。
        Assert.Equal(1.5m, lines[0].Quantity);
        Assert.Equal(1003L, lines[0].UnitPriceCents);
        Assert.Equal("manual-amount", lines[0].DiscountState.Mode);
        Assert.Equal(1505L, lines[0].DiscountState.Cents);

        // catalog/manual provenance。
        Assert.Equal("catalog", lines[0].BasePriceSource);
        Assert.Equal("REF-1", lines[0].SyncProvenance!.ReferenceCode);
        Assert.Equal(0, lines[0].SyncProvenance!.PriceSource);
        Assert.Equal("manual", lines[1].BasePriceSource);
        Assert.Null(lines[1].SyncProvenance!.ReferenceCode);
        Assert.Equal(1, lines[1].SyncProvenance!.PriceSource);

        // manual-percent 与 promotion 折扣 union。
        Assert.Equal("manual-percent", lines[1].DiscountState.Mode);
        Assert.Equal(2500, lines[1].DiscountState.BasisPoints);
        Assert.Equal("promotion", lines[2].DiscountState.Mode);
        Assert.Equal(200L, lines[2].DiscountState.Cents);
        Assert.Equal(["promo-bundle"], lines[2].DiscountState.PromotionIds);

        // 冻结 promotion definition：标量 fixedPriceCents、null/整数 maxApplications、decimal unitWeight。
        Assert.Equal(2, payload.PricingState.Promotions.Count);
        var bundle = payload.PricingState.Promotions[0];
        Assert.Equal("promo-bundle", bundle.Id);
        Assert.Equal("Buy 2 save 10", bundle.Name);
        Assert.Equal("2026-07-01T00:00:00.000Z", bundle.EffectiveStartIso);
        Assert.Equal("2026-07-31T23:59:59.000Z", bundle.EffectiveEndIso);
        Assert.True(bundle.IsExclusive);
        Assert.Equal(2, bundle.Priority);
        Assert.Equal(2, bundle.ApplyQuantity);
        Assert.Equal(2000L, bundle.FixedPriceCents);
        Assert.Equal(1, bundle.MaxApplicationsPerOrder);
        Assert.Equal([new("P-BUNDLE", 1m)], bundle.Products);
        var weighed = payload.PricingState.Promotions[1];
        Assert.Null(weighed.MaxApplicationsPerOrder);
        Assert.Equal([new("P-WEIGHT", 0.25m)], weighed.Products);
    }

    [Theory]
    [InlineData("shared-sale-cart-v1.reject-unknown-field.json")]
    [InlineData("shared-sale-cart-v1.reject-cross-store-envelope.json")]
    [InlineData("shared-sale-cart-v1.reject-summary-envelope.json")]
    [InlineData("shared-sale-cart-v1.reject-duplicate-promotion-id.json")]
    [InlineData("shared-sale-cart-v1.reject-duplicate-line-id.json")]
    [InlineData("shared-sale-cart-v1.reject-gross-overflow.json")]
    [InlineData("shared-sale-cart-v1.reject-unsafe-integer-cents.json")]
    public void Rejection_fixtures_are_rejected_by_strict_parser(string fixtureName)
    {
        var fixture = ReadFixture(fixtureName);

        Assert.Throws<SharedHeldOrderCanonicalValidationException>(
            () => Serializer.Deserialize(fixture));
    }

    [Fact]
    public void Near_max_fixture_is_accepted_and_roundtrip_is_byte_stable()
    {
        var fixture = ReadFixture("shared-sale-cart-v1.accept-near-max-safe-gross.json");

        var payload = Serializer.Deserialize(fixture);

        Assert.Equal(1_000_000m, payload.PricingState.Lines[0].Quantity);
        Assert.Equal(9_007_199_254L, payload.PricingState.Lines[0].UnitPriceCents);
        Assert.Equal(fixture, Serializer.Serialize(payload));
    }
}
