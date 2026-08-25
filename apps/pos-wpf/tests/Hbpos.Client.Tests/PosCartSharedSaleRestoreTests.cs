using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Tests;

/// <summary>
/// 共享 sale 快照恢复专用路径：允许正有限小数数量，其余金额/折扣来源精确恢复；
/// 现有 Add/SetQuantity/RestoreSnapshot 仍严格正整数（回归保护）。
/// </summary>
public sealed class PosCartSharedSaleRestoreTests
{
    [Fact]
    public void RestoreSharedSaleSnapshot_restores_decimal_quantity_and_promotion_discount()
    {
        var cart = new PosCartService();
        var snapshot = new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001",
                "P-1",
                "REF-1",
                "Product 1",
                "CODE-1",
                "ITEM-1",
                null,
                1.5m,
                19.99m,
                4m,
                null,
                PriceSourceKind.StoreRetailPrice,
                "Store Retail Price",
                DiscountSource: PosCartLineDiscountSource.Promotion)
        ]);

        cart.RestoreSharedSaleSnapshot(snapshot);

        var line = Assert.Single(cart.Lines);
        Assert.Equal(1.5m, line.Quantity);
        Assert.Equal(19.99m, line.UnitPrice);
        Assert.Equal(4m, line.DiscountAmount);
        Assert.True(line.IsAutomaticPromotionDiscount);
        Assert.Equal(29.99m, line.GrossAmount);
        Assert.Equal(25.99m, line.ActualAmount);
        Assert.Equal(29.99m, cart.TotalAmount);
        Assert.Equal(25.99m, cart.ActualAmount);
        Assert.True(cart.HasNonIntegerQuantity);
    }

    [Fact]
    public void RestoreSharedSaleSnapshot_rounds_catalog_discount_for_decimal_quantity_away_from_zero()
    {
        var cart = new PosCartService();
        cart.RestoreSharedSaleSnapshot(new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001", "P-CATALOG", null, "Weighted catalog item", "CODE-CATALOG", null, null,
                1.5m, 6.99m, 2.10m, 20m, PriceSourceKind.StoreRetailPrice, "Store Retail Price",
                DiscountSource: PosCartLineDiscountSource.Catalog,
                CatalogDiscountBasisPoints: 2000)
        ]));

        var line = Assert.Single(cart.Lines);
        Assert.Equal(1.5m, line.Quantity);
        Assert.Equal(2.10m, line.DiscountAmount);
        Assert.Equal(8.39m, line.ActualAmount);
        Assert.Equal(8.39m, cart.ActualAmount);
    }

    [Fact]
    public void RestoreSharedSaleSnapshot_bound_decimal_quantity_is_supported_for_checkout()
    {
        var claimId = Guid.NewGuid();
        var cart = new PosCartService();
        cart.RestoreSharedSaleSnapshot(new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001", "P-1", null, "Weighted item", "CODE-1", null, null,
                1.25m, 8m, 0m, null, PriceSourceKind.ProductBase, "Product Base")
        ], claimId));

        Assert.False(cart.HasNonIntegerQuantity);
        Assert.Equal(10m, cart.TotalAmount);
        Assert.Equal(claimId, cart.CreateSnapshot().SharedHeldOrderClaimId);
    }

    [Fact]
    public void Current_catalog_refresh_does_not_overwrite_frozen_shared_promotion_result()
    {
        var now = DateTimeOffset.UtcNow;
        var cart = new PosCartService();
        cart.RestoreSharedSaleSnapshot(new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001", "P-1", null, "Product 1", "CODE-1", null, null,
                2m, 10m, 4m, null, PriceSourceKind.ProductBase, "Product Base",
                DiscountSource: PosCartLineDiscountSource.Promotion)
        ], Guid.NewGuid()));

        cart.SetAutomaticPromotionRules([
            new CatalogPromotionRuleDto(
                "CURRENT-RULE", "Current rule", false, 1, 2, 5m, null,
                now.AddDays(-1), now.AddDays(1), now,
                [new CatalogPromotionProductDto("P-1", 1)])
        ]);

        var line = Assert.Single(cart.Lines);
        Assert.Equal(4m, line.DiscountAmount);
        Assert.Equal(16m, cart.ActualAmount);
        Assert.True(line.IsAutomaticPromotionDiscount);
    }

    [Fact]
    public void Current_catalog_rules_do_not_add_promotion_to_pristine_shared_snapshot_without_frozen_discount()
    {
        var now = DateTimeOffset.UtcNow;
        var cart = new PosCartService();
        cart.RestoreSharedSaleSnapshot(new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001", "P-1", null, "Product 1", "CODE-1", null, null,
                2m, 10m, 0m, null, PriceSourceKind.ProductBase, "Product Base")
        ], Guid.NewGuid()));

        cart.SetAutomaticPromotionRules([
            new CatalogPromotionRuleDto(
                "CURRENT-RULE", "Current rule", false, 1, 2, 15m, null,
                now.AddDays(-1), now.AddDays(1), now,
                [new CatalogPromotionProductDto("P-1", 1)])
        ]);

        var line = Assert.Single(cart.Lines);
        Assert.Equal(0m, line.DiscountAmount);
        Assert.Equal(20m, cart.ActualAmount);

        Assert.True(cart.SetLineQuantity(line, 3m));
        Assert.Equal(5m, line.DiscountAmount);
        Assert.Equal(25m, cart.ActualAmount);
    }

    [Fact]
    public void Remote_price_refresh_cannot_mutate_pristine_shared_snapshot_but_applies_after_user_edit()
    {
        var now = DateTimeOffset.UtcNow;
        var cart = new PosCartService();
        cart.RestoreSharedSaleSnapshot(new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001", "P-1", null, "Product 1", "CODE-1", null, null,
                2m, 10m, 4m, null, PriceSourceKind.ProductBase, "Product Base",
                DiscountSource: PosCartLineDiscountSource.Promotion)
        ], Guid.NewGuid()));
        cart.SetAutomaticPromotionRules([
            new CatalogPromotionRuleDto(
                "CURRENT-RULE", "Current rule", false, 1, 2, 15m, null,
                now.AddDays(-1), now.AddDays(1), now,
                [new CatalogPromotionProductDto("P-1", 1)])
        ]);

        var line = Assert.Single(cart.Lines);
        var remoteItem = new SellableItemDto(
            "S001", "P-1", null, "Product 1", "CODE-1", null, "CODE-1",
            12m, PriceSourceKind.ProductBase, "Product Base", 1m, null, null);

        Assert.False(cart.UpdateLineFromRemote(line, remoteItem));
        Assert.Equal(10m, line.UnitPrice);
        Assert.Equal(4m, line.DiscountAmount);
        Assert.Equal(16m, cart.ActualAmount);

        Assert.True(cart.SetLineQuantity(line, 3m));
        Assert.True(cart.UpdateLineFromRemote(line, remoteItem));
        Assert.Equal(12m, line.UnitPrice);
        Assert.Equal(9m, line.DiscountAmount);
        Assert.Equal(27m, cart.ActualAmount);
        Assert.True(line.IsAutomaticPromotionDiscount);
    }

    [Fact]
    public void User_edits_recalculate_frozen_shared_promotion_against_current_rules()
    {
        var now = DateTimeOffset.UtcNow;
        var cart = new PosCartService();
        cart.RestoreSharedSaleSnapshot(new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001", "P-1", null, "Product 1", "CODE-1", null, null,
                2m, 10m, 4m, null, PriceSourceKind.ProductBase, "Product Base",
                DiscountSource: PosCartLineDiscountSource.Promotion)
        ], Guid.NewGuid()));
        cart.SetAutomaticPromotionRules([
            new CatalogPromotionRuleDto(
                "CURRENT-RULE", "Current rule", false, 1, 2, 15m, null,
                now.AddDays(-1), now.AddDays(1), now,
                [new CatalogPromotionProductDto("P-1", 1)])
        ]);

        var line = Assert.Single(cart.Lines);
        Assert.Equal(4m, line.DiscountAmount); // 目录刷新仍保留冻结快照。

        Assert.True(cart.SetLineQuantity(line, 3m));
        Assert.Equal(5m, line.DiscountAmount);
        Assert.Equal(25m, cart.ActualAmount);

        Assert.True(cart.SetLineUnitPrice(line, 12m));
        Assert.Equal(9m, line.DiscountAmount);
        Assert.Equal(27m, cart.ActualAmount);

        cart.AddItem(new SellableItemDto(
            "S001", "P-1", null, "Product 1", "CODE-1", null, "CODE-1",
            12m, PriceSourceKind.ProductBase, "Product Base", 1m, null, null));
        Assert.Equal(4m, line.Quantity);
        Assert.Equal(18m, line.DiscountAmount);
        Assert.Equal(30m, cart.ActualAmount);
    }

    [Fact]
    public void User_edit_without_current_rules_clears_frozen_shared_promotion()
    {
        var cart = new PosCartService();
        cart.RestoreSharedSaleSnapshot(new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001", "P-1", null, "Product 1", "CODE-1", null, null,
                2m, 10m, 4m, null, PriceSourceKind.ProductBase, "Product Base",
                DiscountSource: PosCartLineDiscountSource.Promotion)
        ], Guid.NewGuid()));

        var line = Assert.Single(cart.Lines);
        Assert.True(cart.SetLineQuantity(line, 3m));

        Assert.False(line.IsAutomaticPromotionDiscount);
        Assert.Equal(0m, line.DiscountAmount);
        Assert.Equal(30m, cart.ActualAmount);
    }

    [Fact]
    public void RestoreSharedSaleSnapshot_restores_manual_percent_and_sets_cart_totals()
    {
        var cart = new PosCartService();
        var snapshot = new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001",
                "P-1",
                null,
                "Product 1",
                "CODE-1",
                null,
                null,
                2m,
                10m,
                2m,
                10m,
                PriceSourceKind.ProductBase,
                "Product Base",
                DiscountSource: PosCartLineDiscountSource.Manual)
        ]);

        cart.RestoreSharedSaleSnapshot(snapshot);

        var line = Assert.Single(cart.Lines);
        Assert.Equal(10m, line.DiscountPercent);
        Assert.Equal(2m, line.DiscountAmount);
        Assert.True(line.HasManualDiscount);
        Assert.Equal(20m, cart.TotalAmount);
        Assert.Equal(18m, cart.ActualAmount);
    }

    [Fact]
    public void RestoreSharedSaleSnapshot_preserves_explicit_claim_binding_until_cart_is_cleared()
    {
        var claimId = Guid.NewGuid();
        var cart = new PosCartService();
        var snapshot = new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001",
                "P-1",
                null,
                "Product 1",
                "CODE-1",
                null,
                null,
                1m,
                10m,
                0m,
                null,
                PriceSourceKind.ProductBase,
                "Product Base")
        ], claimId);

        cart.RestoreSharedSaleSnapshot(snapshot);

        Assert.Equal(claimId, cart.CreateSnapshot().SharedHeldOrderClaimId);
        cart.Clear();
        Assert.Null(cart.CreateSnapshot().SharedHeldOrderClaimId);
    }

    [Fact]
    public void ClearSharedHeldOrderClaim_clears_only_exact_claim_binding()
    {
        var claimId = Guid.NewGuid();
        var cart = new PosCartService();
        cart.RestoreSharedSaleSnapshot(new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001",
                "P-1",
                null,
                "Product 1",
                "CODE-1",
                null,
                null,
                1m,
                10m,
                0m,
                null,
                PriceSourceKind.ProductBase,
                "Product Base")
        ], claimId));

        // 其他 claim（或普通购物车）不得误清。
        Assert.False(cart.ClearSharedHeldOrderClaim(Guid.NewGuid()));
        Assert.Single(cart.Lines);
        Assert.Equal(claimId, cart.CreateSnapshot().SharedHeldOrderClaimId);

        // 精确匹配的 Active 取单购物车才允许整单清空并解绑。
        Assert.True(cart.ClearSharedHeldOrderClaim(claimId));
        Assert.True(cart.IsEmpty);
        Assert.Null(cart.CreateSnapshot().SharedHeldOrderClaimId);
    }

    [Fact]
    public void RestoreSharedSaleSnapshot_rejects_non_sale_lines_and_invalid_quantities()
    {
        var cart = new PosCartService();
        var returnLine = new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001",
                "P-1",
                null,
                "Product 1",
                "CODE-1",
                null,
                null,
                1m,
                10m,
                0m,
                null,
                PriceSourceKind.ProductBase,
                "Product Base",
                Kind: CartLineKind.Return)
        ]);
        Assert.Throws<InvalidOperationException>(() => cart.RestoreSharedSaleSnapshot(returnLine));
        Assert.True(cart.IsEmpty);

        var zeroQuantity = new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001",
                "P-1",
                null,
                "Product 1",
                "CODE-1",
                null,
                null,
                0m,
                10m,
                0m,
                null,
                PriceSourceKind.ProductBase,
                "Product Base")
        ]);
        Assert.Throws<InvalidOperationException>(() => cart.RestoreSharedSaleSnapshot(zeroQuantity));
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public void Existing_quantity_paths_still_reject_decimal_quantities()
    {
        var cart = new PosCartService();
        var item = new SellableItemDto(
            "S001",
            "P-1",
            null,
            "Product 1",
            "CODE-1",
            null,
            "CODE-1",
            10m,
            PriceSourceKind.ProductBase,
            "Product Base",
            1m,
            null,
            null);
        var line = cart.AddItem(item);

        Assert.False(cart.SetLineQuantity(line, 1.5m));
        Assert.Equal(1m, line.Quantity);
        Assert.Throws<InvalidOperationException>(() => line.SetQuantity(1.5m));

        var snapshot = new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001",
                "P-1",
                null,
                "Product 1",
                "CODE-1",
                null,
                null,
                2.5m,
                10m,
                0m,
                null,
                PriceSourceKind.ProductBase,
                "Product Base")
        ]);
        Assert.Throws<InvalidOperationException>(() => cart.RestoreSnapshot(snapshot));
        Assert.False(cart.HasNonIntegerQuantity);
    }

    private static PosCartSnapshot SingleBoundSharedSnapshot(Guid claimId, string lookupCode = "CODE-1")
    {
        return new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001",
                "P-1",
                null,
                "Product 1",
                lookupCode,
                null,
                null,
                1m,
                10m,
                0m,
                null,
                PriceSourceKind.ProductBase,
                "Product Base")
        ], claimId);
    }

    [Fact]
    public void RemoveLine_on_bound_single_line_cart_fails_closed_keeping_line_and_binding()
    {
        var claimId = Guid.NewGuid();
        var cart = new PosCartService();
        cart.RestoreSharedSaleSnapshot(SingleBoundSharedSnapshot(claimId));
        var line = Assert.Single(cart.Lines);

        // 删除唯一行会静默清 binding 并遗留服务端 Active：必须阻止并保持行+binding。
        Assert.False(cart.RemoveLine(line));
        Assert.Single(cart.Lines);
        Assert.Equal(claimId, cart.CreateSnapshot().SharedHeldOrderClaimId);
    }

    [Fact]
    public void RemoveLineByLookupCode_on_bound_single_line_cart_fails_closed()
    {
        var claimId = Guid.NewGuid();
        var cart = new PosCartService();
        cart.RestoreSharedSaleSnapshot(SingleBoundSharedSnapshot(claimId));

        Assert.False(cart.RemoveLineByLookupCode("S001", "CODE-1"));
        Assert.Single(cart.Lines);
        Assert.Equal(claimId, cart.CreateSnapshot().SharedHeldOrderClaimId);
    }

    [Fact]
    public void DecreaseLine_at_minimum_on_bound_single_line_cart_keeps_line_and_binding()
    {
        var claimId = Guid.NewGuid();
        var cart = new PosCartService();
        cart.RestoreSharedSaleSnapshot(SingleBoundSharedSnapshot(claimId));
        var line = Assert.Single(cart.Lines);

        Assert.False(cart.DecreaseLine(line));
        var keptLine = Assert.Single(cart.Lines);
        Assert.Equal(1m, keptLine.Quantity);
        Assert.Equal(claimId, cart.CreateSnapshot().SharedHeldOrderClaimId);
    }

    [Fact]
    public void SetLineQuantity_to_zero_on_bound_cart_fails_closed_keeping_line_and_binding()
    {
        var claimId = Guid.NewGuid();
        var cart = new PosCartService();
        cart.RestoreSharedSaleSnapshot(SingleBoundSharedSnapshot(claimId));
        var line = Assert.Single(cart.Lines);

        Assert.False(cart.SetLineQuantity(line, 0m));
        var keptLine = Assert.Single(cart.Lines);
        Assert.Equal(1m, keptLine.Quantity);
        Assert.Equal(claimId, cart.CreateSnapshot().SharedHeldOrderClaimId);
    }

    [Fact]
    public void Removing_one_of_multiple_lines_keeps_shared_binding()
    {
        var claimId = Guid.NewGuid();
        var cart = new PosCartService();
        cart.RestoreSharedSaleSnapshot(new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001",
                "P-1",
                null,
                "Product 1",
                "CODE-1",
                null,
                null,
                1m,
                10m,
                0m,
                null,
                PriceSourceKind.ProductBase,
                "Product Base"),
            new PosCartLineSnapshot(
                "S001",
                "P-2",
                null,
                "Product 2",
                "CODE-2",
                null,
                null,
                1m,
                20m,
                0m,
                null,
                PriceSourceKind.ProductBase,
                "Product Base")
        ], claimId));

        Assert.True(cart.RemoveLineByLookupCode("S001", "CODE-2"));
        Assert.Single(cart.Lines);
        Assert.Equal(claimId, cart.CreateSnapshot().SharedHeldOrderClaimId);
    }

    [Fact]
    public void Unbound_cart_decrease_keeps_minimum_and_remove_can_empty()
    {
        var cart = new PosCartService();
        var item = new SellableItemDto(
            "S001",
            "P-1",
            null,
            "Product 1",
            "CODE-1",
            null,
            "CODE-1",
            10m,
            PriceSourceKind.ProductBase,
            "Product Base",
            1m,
            null,
            null);
        var line = cart.AddItem(item);

        // 普通购物车同样保持数量下限 1；删除唯一行必须显式走 RemoveLine。
        Assert.False(cart.DecreaseLine(line));
        Assert.Same(line, Assert.Single(cart.Lines));
        Assert.Equal(1m, line.Quantity);
        Assert.True(cart.RemoveLine(line));
        Assert.Empty(cart.Lines);
        Assert.Null(cart.CreateSnapshot().SharedHeldOrderClaimId);
    }
}
