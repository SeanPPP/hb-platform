using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Client.Tests;

public sealed class ShellCatalogServiceTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task SyncCatalogAndReloadAsync_ResetCancelsRunningRegularSyncAndDoesNotOverlapWrites()
    {
        var priceIndex = new LocalSellableItemIndex();
        var repository = new FakeLocalCatalogRepository();
        var sync = new CoordinatedCatalogSyncService();
        var service = new ShellCatalogService(priceIndex, repository, sync);

        var regularTask = service.SyncCatalogAndReloadAsync("S01", forceFullDownload: false);
        await sync.RegularStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var resetTask = service.SyncCatalogAndReloadAsync("S01", forceFullDownload: true);
        await sync.ResetStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        sync.ReleaseRegularIfNotCanceled();
        var regularException = await Record.ExceptionAsync(() => regularTask);
        var resetItems = await resetTask;

        Assert.IsAssignableFrom<OperationCanceledException>(regularException);
        Assert.True(sync.RegularCanceled);
        Assert.Equal(1, sync.MaxActiveWrites);
        Assert.Equal(["S01:false", "S01:true"], sync.Calls);
        Assert.Single(resetItems);
        Assert.Equal("RESET-ITEM", Assert.Single(priceIndex.Items).ProductCode);
    }

    [Fact]
    public async Task IsCatalogSyncActive_IsTrueWhileBackgroundSyncIsRunning()
    {
        var priceIndex = new LocalSellableItemIndex();
        var repository = new FakeLocalCatalogRepository();
        var sync = new CoordinatedCatalogSyncService();
        var service = new ShellCatalogService(priceIndex, repository, sync);

        var regularTask = service.SyncCatalogAndReloadAsync("S01", forceFullDownload: false);
        await sync.RegularStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(service.IsCatalogSyncActive);

        sync.ReleaseRegularIfNotCanceled();
        await regularTask.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.False(service.IsCatalogSyncActive);
    }

    [Fact]
    public async Task LoadLocalCatalogAsync_LoadsAndRebuildsIndexOnBackgroundThread()
    {
        var priceIndex = new LocalSellableItemIndex();
        var repository = new ThreadTrackingLocalCatalogRepository();
        var service = new ShellCatalogService(priceIndex, repository, new CoordinatedCatalogSyncService());

        var callerThreadId = -1;
        var completion = new TaskCompletionSource<IReadOnlyList<SellableItemDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            callerThreadId = Environment.CurrentManagedThreadId;
            try
            {
                _ = service.LoadLocalCatalogAsync("S01").ContinueWith(loadTask =>
                {
                    if (loadTask.IsFaulted)
                    {
                        completion.TrySetException(loadTask.Exception.InnerExceptions);
                        return;
                    }

                    if (loadTask.IsCanceled)
                    {
                        completion.TrySetCanceled();
                        return;
                    }

                    completion.TrySetResult(loadTask.Result);
                }, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        thread.Start();
        var loadedItems = await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Single(loadedItems);
        Assert.Equal("RESET-ITEM", loadedItems[0].ProductCode);
        Assert.NotEqual(callerThreadId, repository.LoadThreadId);
        Assert.Equal("RESET-ITEM", Assert.Single(priceIndex.Items).ProductCode);
    }

    private static SellableItemDto CreateItem(string productCode)
    {
        return new SellableItemDto(
            "S01",
            productCode,
            ReferenceCode: null,
            DisplayName: $"{productCode} item",
            LookupCode: productCode,
            ItemNumber: productCode,
            Barcode: productCode,
            RetailPrice: 1m,
            PriceSourceKind.StoreRetailPrice,
            "store-retail",
            QuantityFactor: 1m,
            UpdatedAt: Timestamp);
    }

    private sealed class CoordinatedCatalogSyncService : ILocalCatalogSyncService
    {
        private readonly TaskCompletionSource _releaseRegular = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeWrites;

        public TaskCompletionSource RegularStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ResetStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Calls { get; } = [];

        public int MaxActiveWrites { get; private set; }

        public bool RegularCanceled { get; private set; }

        public async Task<LocalCatalogSyncResult> FullSyncAsync(
            string storeCode,
            CancellationToken cancellationToken = default,
            IProgress<CatalogSyncProgress>? progress = null,
            bool forceFullDownload = false)
        {
            Calls.Add($"{storeCode}:{forceFullDownload.ToString().ToLowerInvariant()}");
            var activeWrites = Interlocked.Increment(ref _activeWrites);
            MaxActiveWrites = Math.Max(MaxActiveWrites, activeWrites);
            try
            {
                if (forceFullDownload)
                {
                    ResetStarted.SetResult();
                    return new LocalCatalogSyncResult(storeCode, 0, 1, 1, 0);
                }

                RegularStarted.SetResult();
                try
                {
                    await _releaseRegular.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    RegularCanceled = true;
                    throw;
                }

                return new LocalCatalogSyncResult(storeCode, 1, 1, 0, 0);
            }
            finally
            {
                Interlocked.Decrement(ref _activeWrites);
            }
        }

        public void ReleaseRegularIfNotCanceled()
        {
            _releaseRegular.TrySetResult();
        }
    }

    private class FakeLocalCatalogRepository : ILocalCatalogRepository
    {
        public Task ReplaceSellableItemsAsync(IEnumerable<SellableItemDto> items, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpsertSellableItemsAsync(IEnumerable<SellableItemDto> items, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> DeleteByLookupCodesAsync(
            string storeCode,
            IEnumerable<string> lookupCodes,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task<SellableItemDto?> FindByLookupCodeAsync(
            string storeCode,
            string lookupCode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<SellableItemDto?>(null);
        }

        public Task<IReadOnlyList<SellableItemDto>> LoadSpecialProductItemsAsync(
            string storeCode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SellableItemDto>>([]);
        }

        public Task SaveSpecialProductOrderAsync(
            string storeCode,
            IEnumerable<string> productCodes,
            CancellationToken cancellationToken = default)
        {
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

        public Task<int> ClearSpecialProductFlagsExceptAsync(
            string storeCode,
            IEnumerable<string> productCodesToKeep,
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

        public virtual Task<IReadOnlyList<SellableItemDto>> LoadSellableItemsAsync(CancellationToken cancellationToken = default)
        {
            return LoadSellableItemsAsync("S01", cancellationToken);
        }

        public virtual Task<IReadOnlyList<SellableItemDto>> LoadSellableItemsAsync(
            string storeCode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SellableItemDto>>([CreateItem("RESET-ITEM")]);
        }
    }

    private sealed class ThreadTrackingLocalCatalogRepository : FakeLocalCatalogRepository
    {
        public int LoadThreadId { get; private set; }

        public override Task<IReadOnlyList<SellableItemDto>> LoadSellableItemsAsync(
            string storeCode,
            CancellationToken cancellationToken = default)
        {
            LoadThreadId = Environment.CurrentManagedThreadId;
            return base.LoadSellableItemsAsync(storeCode, cancellationToken);
        }
    }
}
