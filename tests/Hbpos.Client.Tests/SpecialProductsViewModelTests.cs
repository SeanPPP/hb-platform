using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Client.Tests;

public sealed class SpecialProductsViewModelTests
{
    [Fact]
    public async Task LoadAsync_shows_special_products_from_local_cache()
    {
        var repository = new FakeCatalogRepository
        {
            SpecialItems = [CreateItem("SKU-001", "Alpha", "930001", isSpecialProduct: true)]
        };
        var viewModel = CreateViewModel(repository: repository);

        await viewModel.LoadAsync();

        var item = Assert.Single(viewModel.SpecialItems);
        Assert.Equal("SKU-001", item.ProductCode);
        Assert.True(item.IsSpecialProduct);
    }

    [Fact]
    public async Task AddToCartCommand_adds_tapped_special_product()
    {
        var cart = new PosCartService();
        var item = CreateItem("SKU-001", "Alpha", "930001", isSpecialProduct: true);
        var repository = new FakeCatalogRepository { SpecialItems = [item] };
        var viewModel = CreateViewModel(cart: cart, repository: repository);
        await viewModel.LoadAsync();

        viewModel.AddToCartCommand.Execute(item);

        var line = Assert.Single(cart.Lines);
        Assert.Equal("SKU-001", line.ProductCode);
        Assert.Equal(1m, line.Quantity);
    }

    [Fact]
    public async Task Offline_add_and_remove_do_not_call_backend()
    {
        var item = CreateItem("SKU-001", "Alpha", "930001", isSpecialProduct: true);
        var repository = new FakeCatalogRepository { SpecialItems = [item] };
        var service = new FakeSpecialProductService();
        var viewModel = CreateViewModel(
            repository: repository,
            service: service,
            session: Session with { IsOnline = false });
        await viewModel.LoadAsync();

        await viewModel.AddSpecialProductCommand.ExecuteAsync(item);
        await viewModel.RemoveSpecialProductCommand.ExecuteAsync(item);

        Assert.Equal(0, service.CallCount);
        Assert.Contains("online", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MoveDown_saves_local_order_without_calling_backend()
    {
        var first = CreateItem("SKU-001", "Alpha", "930001", isSpecialProduct: true);
        var second = CreateItem("SKU-002", "Beta", "930002", isSpecialProduct: true);
        var repository = new FakeCatalogRepository { SpecialItems = [first, second] };
        var service = new FakeSpecialProductService();
        var viewModel = CreateViewModel(repository: repository, service: service);
        await viewModel.LoadAsync();

        await viewModel.MoveDownCommand.ExecuteAsync(first);

        Assert.Equal(["SKU-002", "SKU-001"], viewModel.SpecialItems.Select(x => x.ProductCode).ToArray());
        Assert.Equal(["SKU-002", "SKU-001"], Assert.Single(repository.SavedOrders));
        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task Online_add_calls_service_and_reloads_local_cache()
    {
        var item = CreateItem("SKU-001", "Alpha", "930001");
        var index = new LocalSellableItemIndex();
        index.ReplaceAll([item]);
        var repository = new FakeCatalogRepository
        {
            SellableItems = [item with { IsSpecialProduct = true }],
            SpecialItems = [item with { IsSpecialProduct = true }]
        };
        var service = new FakeSpecialProductService();
        var viewModel = CreateViewModel(index, repository: repository, service: service);

        await viewModel.AddSpecialProductCommand.ExecuteAsync(item);

        Assert.Equal(1, service.CallCount);
        Assert.Equal(("S001", "SKU-001", true), service.LastCall);
        Assert.True(repository.LoadSellableItemsCallCount > 0);
        Assert.Contains(viewModel.SpecialItems, x => x.ProductCode == "SKU-001" && x.IsSpecialProduct);
    }

    private static SpecialProductsViewModel CreateViewModel(
        LocalSellableItemIndex? index = null,
        PosCartService? cart = null,
        FakeCatalogRepository? repository = null,
        FakeSpecialProductService? service = null,
        PosSessionState? session = null)
    {
        return new SpecialProductsViewModel(
            index ?? new LocalSellableItemIndex(),
            cart ?? new PosCartService(),
            repository ?? new FakeCatalogRepository(),
            service ?? new FakeSpecialProductService(),
            session ?? Session,
            new LocalizationService(),
            () => { });
    }

    private static PosSessionState Session => new("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

    private static SellableItemDto CreateItem(
        string productCode,
        string displayName,
        string lookupCode,
        bool isSpecialProduct = false)
    {
        return new SellableItemDto(
            "S001",
            productCode,
            ReferenceCode: null,
            displayName,
            lookupCode,
            ItemNumber: productCode,
            Barcode: lookupCode,
            RetailPrice: 1.25m,
            PriceSourceKind.StoreRetailPrice,
            "store-retail",
            QuantityFactor: 1m,
            UpdatedAt: DateTimeOffset.UtcNow,
            ProductImage: $"https://images.example/{productCode}.jpg",
            DiscountRate: null,
            IsSpecialProduct: isSpecialProduct);
    }

    private sealed class FakeCatalogRepository : ILocalCatalogRepository
    {
        public IReadOnlyList<SellableItemDto> SellableItems { get; init; } = [];

        public IReadOnlyList<SellableItemDto> SpecialItems { get; init; } = [];

        public List<string[]> SavedOrders { get; } = [];

        public int LoadSellableItemsCallCount { get; private set; }

        public Task ReplaceSellableItemsAsync(IEnumerable<SellableItemDto> items, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpsertSellableItemsAsync(IEnumerable<SellableItemDto> items, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> DeleteByLookupCodesAsync(string storeCode, IEnumerable<string> lookupCodes, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task<SellableItemDto?> FindByLookupCodeAsync(string storeCode, string lookupCode, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<SellableItemDto?>(null);
        }

        public Task<IReadOnlyList<SellableItemDto>> LoadSpecialProductItemsAsync(
            string storeCode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SpecialItems);
        }

        public Task SaveSpecialProductOrderAsync(
            string storeCode,
            IEnumerable<string> productCodes,
            CancellationToken cancellationToken = default)
        {
            SavedOrders.Add(productCodes.ToArray());
            return Task.CompletedTask;
        }

        public Task<int> UpdateSpecialProductFlagAsync(
            string storeCode,
            string productCode,
            bool isSpecialProduct,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<LocalSellableItemCompareRow>> LoadSellableItemComparePageAsync(
            string storeCode,
            string? afterLookupCodeNormalized,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalSellableItemCompareRow>>([]);
        }

        public Task<IReadOnlyList<SellableItemDto>> LoadSellableItemsAsync(CancellationToken cancellationToken = default)
        {
            LoadSellableItemsCallCount++;
            return Task.FromResult(SellableItems);
        }
    }

    private sealed class FakeSpecialProductService : ISpecialProductService
    {
        public int CallCount { get; private set; }

        public (string StoreCode, string ProductCode, bool IsSpecialProduct)? LastCall { get; private set; }

        public Task<IReadOnlyList<SellableItemDto>> MarkSpecialProductAsync(
            string storeCode,
            string productCode,
            bool isSpecialProduct,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastCall = (storeCode, productCode, isSpecialProduct);
            return Task.FromResult<IReadOnlyList<SellableItemDto>>([]);
        }
    }
}
