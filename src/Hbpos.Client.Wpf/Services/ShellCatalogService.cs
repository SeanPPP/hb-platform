using Hbpos.Contracts.Catalog;

namespace Hbpos.Client.Wpf.Services;

public delegate IPosTerminalWorkflowService PosTerminalWorkflowFactory(
    Func<string, string, CancellationToken, Task<RemoteLookupRefreshResult>> remoteLookupRefreshAsync,
    Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>> reloadCatalogAsync);

public interface IShellCatalogService
{
    bool IsCatalogSyncActive { get; }

    Task ReplacePreviewCatalogAsync(IEnumerable<SellableItemDto> items, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SellableItemDto>> LoadLocalCatalogAsync(
        string storeCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SellableItemDto>> SyncCatalogAndReloadAsync(
        string storeCode,
        bool forceFullDownload,
        IProgress<CatalogSyncProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ShellCatalogService(
    LocalSellableItemIndex priceIndex,
    ILocalCatalogRepository catalogRepository,
    ILocalCatalogSyncService catalogSync,
    IUiPriorityCoordinator? uiPriorityCoordinator = null) : IShellCatalogService
{
    private readonly object _syncStateGate = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly List<CancellationTokenSource> _regularSyncCts = [];
    private readonly IUiPriorityCoordinator _uiPriorityCoordinator = uiPriorityCoordinator ?? UiPriorityCoordinator.Noop;
    private int _resetRequestCount;
    private int _activeSyncCount;

    public bool IsCatalogSyncActive => Volatile.Read(ref _activeSyncCount) > 0;

    public async Task ReplacePreviewCatalogAsync(
        IEnumerable<SellableItemDto> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var itemList = items as IReadOnlyList<SellableItemDto> ?? items.ToArray();
        await catalogRepository.ReplaceSellableItemsAsync(itemList, cancellationToken);
        priceIndex.ReplaceAll(itemList);
    }

    public async Task<IReadOnlyList<SellableItemDto>> LoadLocalCatalogAsync(
        string storeCode,
        CancellationToken cancellationToken = default)
    {
        var cachedItems = await catalogRepository.LoadSellableItemsAsync(storeCode, cancellationToken);
        priceIndex.ReplaceAll(cachedItems);
        return cachedItems;
    }

    public async Task<IReadOnlyList<SellableItemDto>> SyncCatalogAndReloadAsync(
        string storeCode,
        bool forceFullDownload,
        IProgress<CatalogSyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (forceFullDownload)
        {
            return await ResetCatalogAndReloadAsync(storeCode, progress, cancellationToken);
        }

        return await SyncCatalogAndReloadCoreAsync(storeCode, progress, cancellationToken);
    }

    private async Task<IReadOnlyList<SellableItemDto>> ResetCatalogAndReloadAsync(
        string storeCode,
        IProgress<CatalogSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        lock (_syncStateGate)
        {
            _resetRequestCount++;
            foreach (var regularSyncCts in _regularSyncCts)
            {
                regularSyncCts.Cancel();
            }
        }
        try
        {
            await _syncLock.WaitAsync(cancellationToken);
            try
            {
                return await RunSyncAndReloadOnBackgroundAsync(
                    storeCode,
                    forceFullDownload: true,
                    progress,
                    cancellationToken);
            }
            finally
            {
                _syncLock.Release();
            }
        }
        finally
        {
            lock (_syncStateGate)
            {
                _resetRequestCount--;
            }
        }
    }

    private async Task<IReadOnlyList<SellableItemDto>> SyncCatalogAndReloadCoreAsync(
        string storeCode,
        IProgress<CatalogSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var regularSyncCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_syncStateGate)
        {
            if (_resetRequestCount > 0)
            {
                throw new OperationCanceledException("Catalog sync canceled because catalog reset is pending.");
            }

            _regularSyncCts.Add(regularSyncCts);
        }

        var lockAcquired = false;
        try
        {
            await _syncLock.WaitAsync(regularSyncCts.Token);
            lockAcquired = true;
            lock (_syncStateGate)
            {
                if (_resetRequestCount > 0)
                {
                    throw new OperationCanceledException("Catalog sync canceled because catalog reset is pending.");
                }
            }

            return await RunSyncAndReloadOnBackgroundAsync(
                storeCode,
                forceFullDownload: false,
                progress,
                regularSyncCts.Token);
        }
        finally
        {
            if (lockAcquired)
            {
                _syncLock.Release();
            }

            lock (_syncStateGate)
            {
                _regularSyncCts.Remove(regularSyncCts);
            }
        }
    }

    private Task<IReadOnlyList<SellableItemDto>> RunSyncAndReloadOnBackgroundAsync(
        string storeCode,
        bool forceFullDownload,
        IProgress<CatalogSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            Interlocked.Increment(ref _activeSyncCount);
            try
            {
                await catalogSync.FullSyncAsync(storeCode, cancellationToken, progress, forceFullDownload)
                    .ConfigureAwait(false);
                await _uiPriorityCoordinator.WaitForUiIdleAsync(cancellationToken)
                    .ConfigureAwait(false);
                var cachedItems = await catalogRepository.LoadSellableItemsAsync(storeCode, cancellationToken)
                    .ConfigureAwait(false);
                await _uiPriorityCoordinator.WaitForUiIdleAsync(cancellationToken)
                    .ConfigureAwait(false);
                priceIndex.ReplaceAll(cachedItems);
                return cachedItems;
            }
            finally
            {
                Interlocked.Decrement(ref _activeSyncCount);
            }
        }, cancellationToken);
    }
}
