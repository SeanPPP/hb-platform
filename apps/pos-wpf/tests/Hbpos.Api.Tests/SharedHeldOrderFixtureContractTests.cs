using System.Text.Json;
using Hbpos.Contracts.HeldOrders;

namespace Hbpos.Api.Tests;

/// <summary>
/// 跨端 frozen wire fixture lane（C# 契约 DTO + SharedSaleCartV1Validator 侧，net9.0 可在 Mac 运行）：
/// 与 iPad shared-sale-cart-v1-fixture.test.ts / WPF SharedHeldOrderCanonicalFixtureTests.cs
/// 共用 test-fixtures/shared-held-orders/ 同一批 JSON。
/// 说明：DTO 层 JsonSerializer 默认跳过未知字段，因此未知字段/跨店/summary 信封的拒绝证据
/// 由两端 strict client parser 负责（iPad normalize 与 WPF SharedHeldOrderCanonicalJsonSerializer）；
/// 本 lane 只对 DTO 反序列化后可见的语义边界（重复 id、MAX_SAFE_INTEGER 溢出、越界金额）做 validator 证据。
/// </summary>
public sealed class SharedHeldOrderFixtureContractTests
{
    private static readonly JsonSerializerOptions CamelCase =
        new() { PropertyNameCaseInsensitive = true };

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(
                    Path.Combine(current.FullName, "test-fixtures", "shared-held-orders")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "未找到 test-fixtures/shared-held-orders：" + AppContext.BaseDirectory);
    }

    private static SharedSaleCartV1 DeserializeFixture(string name)
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "test-fixtures",
            "shared-held-orders",
            name);
        return JsonSerializer.Deserialize<SharedSaleCartV1>(
            File.ReadAllText(path),
            CamelCase)!;
    }

    [Fact]
    public void Canonical_fixture_passes_contract_validator_with_decimal_away_from_zero_and_frozen_promotions()
    {
        var cart = DeserializeFixture("shared-sale-cart-v1.canonical.json");

        SharedSaleCartV1Validator.Validate(cart);

        var lines = cart.PricingState.Lines;
        Assert.Equal(1.5m, lines[0].Quantity);
        Assert.Equal(1003L, lines[0].UnitPriceCents);
        Assert.Equal(1505L, lines[0].DiscountState.Cents);
        Assert.Equal("manual-percent", lines[1].DiscountState.Mode);
        Assert.Equal(2500, lines[1].DiscountState.BasisPoints);
        Assert.Equal("promotion", lines[2].DiscountState.Mode);
        Assert.Equal(["promo-bundle"], lines[2].DiscountState.PromotionIds);
        Assert.Equal(2000L, cart.PricingState.Promotions[0].FixedPriceCents);
        Assert.Equal(0.25m, cart.PricingState.Promotions[1].Products[0].UnitWeight);
    }

    [Theory]
    [InlineData("shared-sale-cart-v1.reject-duplicate-promotion-id.json", "promotion id must be unique")]
    [InlineData("shared-sale-cart-v1.reject-duplicate-line-id.json", "lineId must be unique")]
    [InlineData("shared-sale-cart-v1.reject-gross-overflow.json", "gross")]
    [InlineData("shared-sale-cart-v1.reject-unsafe-integer-cents.json", "unitPriceCents")]
    public void Rejection_fixtures_visible_to_dto_are_rejected_by_validator(
        string fixtureName,
        string expectedFragment)
    {
        var cart = DeserializeFixture(fixtureName);

        var errors = SharedSaleCartV1Validator.ValidateAll(cart);

        Assert.Contains(errors, error => error.Contains(expectedFragment, StringComparison.Ordinal));
    }

    [Fact]
    public void Near_max_fixture_passes_contract_validator()
    {
        var cart = DeserializeFixture("shared-sale-cart-v1.accept-near-max-safe-gross.json");

        Assert.Empty(SharedSaleCartV1Validator.ValidateAll(cart));
        Assert.Equal(1_000_000m, cart.PricingState.Lines[0].Quantity);
        Assert.Equal(9_007_199_254L, cart.PricingState.Lines[0].UnitPriceCents);
    }
}
