using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.Catalog;
using CommunityToolkit.Mvvm.Input;

namespace Hbpos.Client.Tests;

public sealed class SharedHeldOrderClearCartViewModelTests
{
    [Fact]
    public async Task Clear_cart_with_shared_binding_releases_before_clearing()
    {
        var claimId = Guid.NewGuid();
        var cart = BoundCart(claimId);
        var calls = new List<Guid>();
        using var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            cart,
            Session(),
            onOpenPayment: null,
            releaseSharedHeldOrderAsync: (actualClaimId, _, _) =>
            {
                calls.Add(actualClaimId);
                Assert.True(cart.ClearSharedHeldOrderClaim(actualClaimId));
                return Task.CompletedTask;
            });

        await Assert.IsAssignableFrom<IAsyncRelayCommand>(viewModel.ClearCartCommand)
            .ExecuteAsync(null);

        Assert.Equal([claimId], calls);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task Clear_cart_release_failure_preserves_bound_cart()
    {
        var claimId = Guid.NewGuid();
        var cart = BoundCart(claimId);
        using var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            cart,
            Session(),
            onOpenPayment: null,
            releaseSharedHeldOrderAsync: (_, _, _) =>
                throw new InvalidOperationException("release failed"));

        await Assert.IsAssignableFrom<IAsyncRelayCommand>(viewModel.ClearCartCommand)
            .ExecuteAsync(null);

        Assert.Single(cart.Lines);
        Assert.Equal(claimId, cart.CreateSnapshot().SharedHeldOrderClaimId);
        Assert.Equal("pos.status.sharedHeldOrderReleaseFailed", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Remove_last_restored_shared_line_releases_claim_before_removing()
    {
        var claimId = Guid.NewGuid();
        var cart = BoundCart(claimId);
        var line = Assert.Single(cart.Lines);
        var calls = new List<Guid>();
        using var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            cart,
            Session(),
            onOpenPayment: null,
            releaseSharedHeldOrderAsync: (actualClaimId, _, _) =>
            {
                calls.Add(actualClaimId);
                Assert.True(cart.ClearSharedHeldOrderClaim(actualClaimId));
                return Task.CompletedTask;
            });

        await Assert.IsAssignableFrom<IAsyncRelayCommand>(viewModel.RemoveLineCommand)
            .ExecuteAsync(line);

        Assert.Equal([claimId], calls);
        Assert.True(cart.IsEmpty);
        Assert.Equal("pos.status.ready", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Decrease_last_restored_shared_line_keeps_claim_without_releasing()
    {
        var claimId = Guid.NewGuid();
        var cart = BoundCart(claimId);
        var line = Assert.Single(cart.Lines);
        var calls = new List<Guid>();
        using var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            cart,
            Session(),
            onOpenPayment: null,
            releaseSharedHeldOrderAsync: (actualClaimId, _, _) =>
            {
                calls.Add(actualClaimId);
                Assert.True(cart.ClearSharedHeldOrderClaim(actualClaimId));
                return Task.CompletedTask;
            });

        Assert.False(viewModel.DecreaseLineCommand.CanExecute(line));

        await Assert.IsAssignableFrom<IAsyncRelayCommand>(viewModel.DecreaseLineCommand)
            .ExecuteAsync(line);

        Assert.Empty(calls);
        Assert.Same(line, Assert.Single(cart.Lines));
        Assert.Equal(1m, line.Quantity);
        Assert.Equal(claimId, cart.CreateSnapshot().SharedHeldOrderClaimId);
    }

    private static PosCartService BoundCart(Guid claimId)
    {
        var cart = new PosCartService();
        cart.RestoreSharedSaleSnapshot(new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001", "P-1", null, "Product 1", "CODE-1", null, null,
                1m, 10m, 0m, null, PriceSourceKind.ProductBase, "Product Base")
        ], claimId));
        return cart;
    }

    private static PosSessionState Session()
    {
        return new PosSessionState(
            "HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);
    }
}
