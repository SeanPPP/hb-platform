using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Client.Wpf.ViewModels;

public sealed partial class SpecialProductsViewModel : ObservableObject
{
    private const int PageSize = 20;

    private readonly LocalSellableItemIndex _priceIndex;
    private readonly PosCartService _cart;
    private readonly ILocalCatalogRepository _catalogRepository;
    private readonly ISpecialProductService _specialProductService;
    private readonly ILocalizationService _localization;
    private readonly Action _onBack;
    private readonly Action<CartLine>? _onCartLineAdded;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private Task? _preloadTask;
    private bool _hasLoaded;
    private string? _loadedStoreCode;

    [ObservableProperty]
    private PosSessionState _session;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isDownloadProgressVisible;

    [ObservableProperty]
    private bool _isDownloadProgressFailed;

    [ObservableProperty]
    private double _downloadProgressValue;

    [ObservableProperty]
    private string _downloadProgressText = string.Empty;

    [ObservableProperty]
    private string _downloadProgressDetailText = string.Empty;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private int _currentPage = 1;

    public SpecialProductsViewModel(
        LocalSellableItemIndex priceIndex,
        PosCartService cart,
        ILocalCatalogRepository catalogRepository,
        ISpecialProductService specialProductService,
        PosSessionState session,
        ILocalizationService localization,
        Action onBack,
        Action<CartLine>? onCartLineAdded = null)
    {
        _priceIndex = priceIndex;
        _cart = cart;
        _catalogRepository = catalogRepository;
        _specialProductService = specialProductService;
        _session = session;
        _localization = localization;
        _onBack = onBack;
        _onCartLineAdded = onCartLineAdded;

        BackCommand = new RelayCommand(_onBack);
        SearchCommand = new RelayCommand(SearchCatalog);
        ClearSearchCommand = new RelayCommand(ClearSearch, () => !string.IsNullOrWhiteSpace(SearchText));
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        DownloadCommand = new AsyncRelayCommand(DownloadSpecialProductsAsync, CanDownloadSpecialProducts);
        ToggleEditModeCommand = new RelayCommand(ToggleEditMode, () => !IsBusy);
        PreviousPageCommand = new RelayCommand(ShowPreviousPage, CanShowPreviousPage);
        NextPageCommand = new RelayCommand(ShowNextPage, CanShowNextPage);
        AddToCartCommand = new RelayCommand<SellableItemDto>(AddToCart);
        AddSpecialProductCommand = new AsyncRelayCommand<SellableItemDto>(AddSpecialProductAsync, CanMutateItem);
        RemoveSpecialProductCommand = new AsyncRelayCommand<SellableItemDto>(RemoveSpecialProductAsync, CanMutateItem);
        MoveUpCommand = new AsyncRelayCommand<SellableItemDto>(item => MoveSpecialProductAsync(item, -1), CanMoveUp);
        MoveDownCommand = new AsyncRelayCommand<SellableItemDto>(item => MoveSpecialProductAsync(item, 1), CanMoveDown);

        _localization.CultureChanged += (_, _) => RaiseLocalizedProperties();
        StatusMessage = T("specialProducts.status.ready");
    }

    public ObservableCollection<SellableItemDto> SpecialItems { get; } = [];

    public ObservableCollection<SellableItemDto> PagedSpecialItems { get; } = [];

    public ObservableCollection<SellableItemDto> SearchResults { get; } = [];

    public IRelayCommand BackCommand { get; }

    public IRelayCommand SearchCommand { get; }

    public IRelayCommand ClearSearchCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand DownloadCommand { get; }

    public IRelayCommand ToggleEditModeCommand { get; }

    public IRelayCommand PreviousPageCommand { get; }

    public IRelayCommand NextPageCommand { get; }

    public IRelayCommand<SellableItemDto> AddToCartCommand { get; }

    public IAsyncRelayCommand<SellableItemDto> AddSpecialProductCommand { get; }

    public IAsyncRelayCommand<SellableItemDto> RemoveSpecialProductCommand { get; }

    public IAsyncRelayCommand<SellableItemDto> MoveUpCommand { get; }

    public IAsyncRelayCommand<SellableItemDto> MoveDownCommand { get; }

    public string TitleText => T("specialProducts.title");

    public string SubtitleText => string.Format(
        _localization.CurrentCulture,
        T("specialProducts.subtitle"),
        Session.StoreName,
        Session.StoreCode);

    public string BackText => T("specialProducts.back");

    public string SearchPlaceholderText => T("specialProducts.search.placeholder");

    public string SearchButtonText => T("specialProducts.search.action");

    public string ClearSearchText => T("Clear");

    public string RefreshText => T("specialProducts.refresh");

    public string DownloadText => T("specialProducts.download");

    public string EditModeText => T(IsEditMode ? "specialProducts.done" : "specialProducts.edit");

    public string PreviousPageText => T("specialProducts.previousPage");

    public string NextPageText => T("specialProducts.nextPage");

    public string AddText => T("specialProducts.add");

    public string RemoveText => T("specialProducts.remove");

    public string MoveUpText => T("specialProducts.moveUp");

    public string MoveDownText => T("specialProducts.moveDown");

    public string TapToAddText => T("specialProducts.tapToAdd");

    public string SearchResultsText => T("specialProducts.search.results");

    public string EmptyText => T("specialProducts.empty");

    public string NoSearchResultsText => T("specialProducts.search.empty");

    public string OnlineStateText => T(Session.IsOnline ? "pos.status.online" : "pos.status.offline");

    public bool HasSearchResults => SearchResults.Count > 0;

    public bool IsSpecialListEmpty => SpecialItems.Count == 0;

    public int TotalPages => Math.Max(1, (SpecialItems.Count + PageSize - 1) / PageSize);

    public string PageStatusText => string.Format(
        _localization.CurrentCulture,
        T("specialProducts.pageStatus"),
        CurrentPage,
        TotalPages,
        SpecialItems.Count);

    public Task PreloadAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoadedForCurrentStore())
        {
            Log($"preload skipped store={Session.StoreCode} reason=already-loaded");
            return Task.CompletedTask;
        }

        if (_preloadTask is null || _preloadTask.IsCompleted)
        {
            Log($"preload start store={Session.StoreCode}");
            _preloadTask = LoadSpecialProductsAsync(
                forceReload: false,
                showBusy: false,
                resetToFirstPage: true,
                cancellationToken);
        }

        return _preloadTask;
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoadedForCurrentStore())
        {
            Log($"ensure loaded skipped store={Session.StoreCode} reason=already-loaded");
            return;
        }

        var preloadTask = _preloadTask;
        if (preloadTask is not null && !preloadTask.IsCompleted)
        {
            Log($"ensure loaded waiting preload store={Session.StoreCode}");
            IsBusy = true;
            try
            {
                await preloadTask;
            }
            finally
            {
                IsBusy = false;
            }

            if (IsLoadedForCurrentStore())
            {
                Log($"ensure loaded completed from preload store={Session.StoreCode}");
                return;
            }
        }

        await LoadSpecialProductsAsync(
            forceReload: false,
            showBusy: true,
            resetToFirstPage: SpecialItems.Count == 0,
            cancellationToken);
    }

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        return LoadSpecialProductsAsync(
            forceReload: true,
            showBusy: true,
            resetToFirstPage: true,
            cancellationToken);
    }

    private async Task LoadSpecialProductsAsync(
        bool forceReload,
        bool showBusy,
        bool resetToFirstPage,
        CancellationToken cancellationToken)
    {
        if (showBusy && IsBusy)
        {
            Log($"load skipped store={Session.StoreCode} reason=busy forceReload={forceReload} showBusy={showBusy}");
            return;
        }

        if (!forceReload && IsLoadedForCurrentStore())
        {
            Log($"load skipped store={Session.StoreCode} reason=already-loaded forceReload={forceReload} showBusy={showBusy}");
            return;
        }

        if (forceReload)
        {
            _preloadTask = null;
        }

        if (showBusy)
        {
            IsBusy = true;
        }

        var totalStopwatch = Stopwatch.StartNew();
        var lockWaitElapsedMs = 0L;
        try
        {
            Log($"load start store={Session.StoreCode} forceReload={forceReload} showBusy={showBusy} resetToFirstPage={resetToFirstPage}");
            var lockStopwatch = Stopwatch.StartNew();
            await _loadLock.WaitAsync(cancellationToken);
            lockStopwatch.Stop();
            lockWaitElapsedMs = lockStopwatch.ElapsedMilliseconds;
            try
            {
                if (!forceReload && IsLoadedForCurrentStore())
                {
                    totalStopwatch.Stop();
                    Log($"load skipped inside lock store={Session.StoreCode} reason=already-loaded lockWaitElapsedMs={lockWaitElapsedMs} totalElapsedMs={totalStopwatch.ElapsedMilliseconds}");
                    return;
                }

                var loadStopwatch = Stopwatch.StartNew();
                var specialItems = await _catalogRepository.LoadSpecialProductItemsAsync(Session.StoreCode, cancellationToken);
                loadStopwatch.Stop();

                var replaceStopwatch = Stopwatch.StartNew();
                SpecialItems.ReplaceWith(specialItems);
                replaceStopwatch.Stop();
                _hasLoaded = true;
                _loadedStoreCode = NormalizeStoreCode(Session.StoreCode);

                var pageStopwatch = Stopwatch.StartNew();
                RefreshPagedSpecialItems(resetToFirstPage: resetToFirstPage);
                pageStopwatch.Stop();
                if (showBusy)
                {
                    SetStatus("specialProducts.status.loaded", SpecialItems.Count);
                }

                totalStopwatch.Stop();
                Log($"load completed store={Session.StoreCode} items={specialItems.Count} lockWaitElapsedMs={lockWaitElapsedMs} loadElapsedMs={loadStopwatch.ElapsedMilliseconds} replaceElapsedMs={replaceStopwatch.ElapsedMilliseconds} pageRefreshElapsedMs={pageStopwatch.ElapsedMilliseconds} totalElapsedMs={totalStopwatch.ElapsedMilliseconds}");
            }
            finally
            {
                _loadLock.Release();
            }

            RefreshCommandStates();
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            _hasLoaded = false;
            Log($"load failed store={Session.StoreCode} lockWaitElapsedMs={lockWaitElapsedMs} totalElapsedMs={totalStopwatch.ElapsedMilliseconds} error={ex.Message}");
            if (showBusy)
            {
                SetStatusText(string.Format(_localization.CurrentCulture, T("specialProducts.status.loadFailed"), ex.Message));
            }
            else
            {
                ConsoleLog.Write("SpecialProducts", $"preload failed store={Session.StoreCode} error={ex.Message}");
            }
        }
        finally
        {
            if (showBusy)
            {
                IsBusy = false;
            }
        }
    }

    partial void OnSessionChanged(PosSessionState value)
    {
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(OnlineStateText));
        RefreshCommandStates();
    }

    partial void OnSearchTextChanged(string value)
    {
        ClearSearchCommand.NotifyCanExecuteChanged();
        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults.Clear();
            OnPropertyChanged(nameof(HasSearchResults));
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommandStates();
    }

    partial void OnIsEditModeChanged(bool value)
    {
        OnPropertyChanged(nameof(EditModeText));
        if (!value)
        {
            ClearSearch();
        }

        RefreshCommandStates();
    }

    private void SearchCatalog()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchResults.Clear();
            OnPropertyChanged(nameof(HasSearchResults));
            return;
        }

        var results = _priceIndex.Search(Session.StoreCode, SearchText, 80)
            .Where(item =>
                string.Equals(item.StoreCode, Session.StoreCode, StringComparison.OrdinalIgnoreCase) &&
                !item.IsSpecialProduct)
            .GroupBy(item => NormalizeProductCode(item.ProductCode), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(PreferredLookupRank)
                .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.LookupCode, StringComparer.OrdinalIgnoreCase)
                .First())
            .Take(12)
            .ToArray();

        SearchResults.ReplaceWith(results);
        OnPropertyChanged(nameof(HasSearchResults));
        SetStatus(results.Length == 0
            ? "specialProducts.status.noSearchResults"
            : "specialProducts.status.searchResults",
            results.Length);
    }

    private void ClearSearch()
    {
        SearchText = string.Empty;
        SearchResults.Clear();
        OnPropertyChanged(nameof(HasSearchResults));
    }

    private void AddToCart(SellableItemDto? item)
    {
        if (item is null)
        {
            Log($"operation=add-to-cart store={Session.StoreCode} success=false reason=null-item totalElapsedMs=0");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var line = _cart.AddItem(item);
            SetStatus("specialProducts.status.addedToCart", item.DisplayName);
            _onBack();
            _onCartLineAdded?.Invoke(line);
            stopwatch.Stop();
            Log($"operation=add-to-cart store={Session.StoreCode} productCode={item.ProductCode} lookupCode={item.LookupCode} success=true revealRequested={_onCartLineAdded is not null} cartLines={_cart.Lines.Count} totalElapsedMs={stopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Log($"operation=add-to-cart store={Session.StoreCode} productCode={item.ProductCode} lookupCode={item.LookupCode} success=false totalElapsedMs={stopwatch.ElapsedMilliseconds} error={ex.Message}");
            throw;
        }
    }

    private async Task DownloadSpecialProductsAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || !EnsureOnlineMutationAllowed())
        {
            return;
        }

        IsBusy = true;
        var totalStopwatch = Stopwatch.StartNew();
        try
        {
            Log($"operation=download store={Session.StoreCode} stage=start");
            ApplyDownloadProgress(new SpecialProductDownloadProgress(
                Session.StoreCode,
                SpecialProductDownloadProgressStage.Preparing,
                0,
                0,
                0,
                0,
                0,
                0,
                0));

            var progress = new Progress<SpecialProductDownloadProgress>(ApplyDownloadProgress);
            var serviceStopwatch = Stopwatch.StartNew();
            var result = await _specialProductService.DownloadSpecialProductsAsync(
                Session.StoreCode,
                cancellationToken,
                progress);
            serviceStopwatch.Stop();

            var loadCatalogStopwatch = Stopwatch.StartNew();
            var catalogItems = await _catalogRepository.LoadSellableItemsAsync(Session.StoreCode, cancellationToken);
            loadCatalogStopwatch.Stop();

            var indexStopwatch = Stopwatch.StartNew();
            _priceIndex.ReplaceAll(catalogItems);
            indexStopwatch.Stop();

            var loadSpecialStopwatch = Stopwatch.StartNew();
            var specialItems = await _catalogRepository.LoadSpecialProductItemsAsync(Session.StoreCode, cancellationToken);
            loadSpecialStopwatch.Stop();

            var replaceStopwatch = Stopwatch.StartNew();
            SpecialItems.ReplaceWith(specialItems);
            replaceStopwatch.Stop();
            _hasLoaded = true;
            _loadedStoreCode = NormalizeStoreCode(Session.StoreCode);

            var searchStopwatch = Stopwatch.StartNew();
            SearchCatalog();
            searchStopwatch.Stop();

            var pageStopwatch = Stopwatch.StartNew();
            RefreshPagedSpecialItems(resetToFirstPage: true);
            pageStopwatch.Stop();
            ApplyDownloadProgress(new SpecialProductDownloadProgress(
                result.StoreCode,
                SpecialProductDownloadProgressStage.Completed,
                result.TotalCount,
                result.DownloadedCount,
                100,
                result.PageCount,
                result.UpsertedCount,
                result.UnmarkedCount,
                0));
            SetStatus(
                "specialProducts.status.downloadCompleted",
                result.DownloadedCount,
                result.UnmarkedCount);
            totalStopwatch.Stop();
            Log($"operation=download store={Session.StoreCode} stage=completed pages={result.PageCount} downloaded={result.DownloadedCount} upserted={result.UpsertedCount} unmarked={result.UnmarkedCount} serviceElapsedMs={serviceStopwatch.ElapsedMilliseconds} loadCatalogElapsedMs={loadCatalogStopwatch.ElapsedMilliseconds} indexRefreshElapsedMs={indexStopwatch.ElapsedMilliseconds} loadSpecialElapsedMs={loadSpecialStopwatch.ElapsedMilliseconds} replaceElapsedMs={replaceStopwatch.ElapsedMilliseconds} searchElapsedMs={searchStopwatch.ElapsedMilliseconds} pageRefreshElapsedMs={pageStopwatch.ElapsedMilliseconds} totalElapsedMs={totalStopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            Log($"operation=download store={Session.StoreCode} stage=failed totalElapsedMs={totalStopwatch.ElapsedMilliseconds} error={ex.Message}");
            SetStatusText(string.Format(
                _localization.CurrentCulture,
                T("specialProducts.status.downloadFailed"),
                ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddSpecialProductAsync(SellableItemDto? item, CancellationToken cancellationToken)
    {
        if (item is null || !IsEditMode || !EnsureOnlineMutationAllowed())
        {
            return;
        }

        await MarkSpecialProductAsync(item, true, cancellationToken);
        SearchCatalog();
    }

    private async Task RemoveSpecialProductAsync(SellableItemDto? item, CancellationToken cancellationToken)
    {
        if (item is null || !IsEditMode || !EnsureOnlineMutationAllowed())
        {
            return;
        }

        await MarkSpecialProductAsync(item, false, cancellationToken);
    }

    private async Task MarkSpecialProductAsync(
        SellableItemDto item,
        bool isSpecialProduct,
        CancellationToken cancellationToken)
    {
        IsBusy = true;
        var totalStopwatch = Stopwatch.StartNew();
        try
        {
            Log($"operation=mark store={Session.StoreCode} productCode={item.ProductCode} isSpecialProduct={isSpecialProduct} stage=start");
            var serviceStopwatch = Stopwatch.StartNew();
            await _specialProductService.MarkSpecialProductAsync(
                Session.StoreCode,
                item.ProductCode,
                isSpecialProduct,
                cancellationToken);
            serviceStopwatch.Stop();

            var loadCatalogStopwatch = Stopwatch.StartNew();
            var catalogItems = await _catalogRepository.LoadSellableItemsAsync(Session.StoreCode, cancellationToken);
            loadCatalogStopwatch.Stop();

            var indexStopwatch = Stopwatch.StartNew();
            _priceIndex.ReplaceAll(catalogItems);
            indexStopwatch.Stop();

            var loadSpecialStopwatch = Stopwatch.StartNew();
            var specialItems = await _catalogRepository.LoadSpecialProductItemsAsync(Session.StoreCode, cancellationToken);
            loadSpecialStopwatch.Stop();

            var replaceStopwatch = Stopwatch.StartNew();
            SpecialItems.ReplaceWith(specialItems);
            replaceStopwatch.Stop();
            _hasLoaded = true;
            _loadedStoreCode = NormalizeStoreCode(Session.StoreCode);

            var pageStopwatch = Stopwatch.StartNew();
            RefreshPagedSpecialItems(focusProductCode: isSpecialProduct ? item.ProductCode : null);
            pageStopwatch.Stop();
            SetStatus(
                isSpecialProduct ? "specialProducts.status.marked" : "specialProducts.status.unmarked",
                item.DisplayName);
            totalStopwatch.Stop();
            Log($"operation=mark store={Session.StoreCode} productCode={item.ProductCode} isSpecialProduct={isSpecialProduct} stage=completed items={specialItems.Count} serviceElapsedMs={serviceStopwatch.ElapsedMilliseconds} loadCatalogElapsedMs={loadCatalogStopwatch.ElapsedMilliseconds} indexRefreshElapsedMs={indexStopwatch.ElapsedMilliseconds} loadSpecialElapsedMs={loadSpecialStopwatch.ElapsedMilliseconds} replaceElapsedMs={replaceStopwatch.ElapsedMilliseconds} pageRefreshElapsedMs={pageStopwatch.ElapsedMilliseconds} totalElapsedMs={totalStopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            Log($"operation=mark store={Session.StoreCode} productCode={item.ProductCode} isSpecialProduct={isSpecialProduct} stage=failed totalElapsedMs={totalStopwatch.ElapsedMilliseconds} error={ex.Message}");
            SetStatusText(string.Format(_localization.CurrentCulture, T("specialProducts.status.markFailed"), ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MoveSpecialProductAsync(SellableItemDto? item, int delta)
    {
        if (item is null || IsBusy || !IsEditMode)
        {
            return;
        }

        var currentIndex = SpecialItems.IndexOf(item);
        var nextIndex = currentIndex + delta;
        if (currentIndex < 0 || nextIndex < 0 || nextIndex >= SpecialItems.Count)
        {
            return;
        }

        var totalStopwatch = Stopwatch.StartNew();
        SpecialItems.Move(currentIndex, nextIndex);
        var saveStopwatch = Stopwatch.StartNew();
        await _catalogRepository.SaveSpecialProductOrderAsync(
            Session.StoreCode,
            SpecialItems.Select(x => x.ProductCode),
            CancellationToken.None);
        saveStopwatch.Stop();

        var pageStopwatch = Stopwatch.StartNew();
        RefreshPagedSpecialItems(focusProductCode: item.ProductCode);
        pageStopwatch.Stop();
        SetStatus("specialProducts.status.orderSaved");
        RefreshCommandStates();
        totalStopwatch.Stop();
        Log($"operation=move store={Session.StoreCode} productCode={item.ProductCode} fromIndex={currentIndex} toIndex={nextIndex} saveElapsedMs={saveStopwatch.ElapsedMilliseconds} pageRefreshElapsedMs={pageStopwatch.ElapsedMilliseconds} totalElapsedMs={totalStopwatch.ElapsedMilliseconds}");
    }

    private void ToggleEditMode()
    {
        IsEditMode = !IsEditMode;
    }

    private void ShowPreviousPage()
    {
        if (!CanShowPreviousPage())
        {
            return;
        }

        CurrentPage--;
        RefreshPagedSpecialItems();
    }

    private void ShowNextPage()
    {
        if (!CanShowNextPage())
        {
            return;
        }

        CurrentPage++;
        RefreshPagedSpecialItems();
    }

    private void RefreshPagedSpecialItems(string? focusProductCode = null, bool resetToFirstPage = false)
    {
        if (resetToFirstPage)
        {
            CurrentPage = 1;
        }
        else if (!string.IsNullOrWhiteSpace(focusProductCode))
        {
            var focusIndex = SpecialItems
                .Select((item, index) => new { item.ProductCode, Index = index })
                .FirstOrDefault(x => string.Equals(
                    NormalizeProductCode(x.ProductCode),
                    NormalizeProductCode(focusProductCode),
                    StringComparison.OrdinalIgnoreCase))
                ?.Index;

            if (focusIndex.HasValue)
            {
                CurrentPage = focusIndex.Value / PageSize + 1;
            }
        }

        CurrentPage = Math.Clamp(CurrentPage, 1, TotalPages);
        PagedSpecialItems.ReplaceWith(SpecialItems
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize));
        OnPropertyChanged(nameof(IsSpecialListEmpty));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageStatusText));
        RefreshCommandStates();
    }

    private bool EnsureOnlineMutationAllowed()
    {
        if (Session.IsOnline)
        {
            return true;
        }

        SetStatus("specialProducts.status.onlineRequired");
        return false;
    }

    private bool CanMutateItem(SellableItemDto? item)
    {
        return item is not null && IsEditMode && Session.IsOnline && !IsBusy;
    }

    private bool CanDownloadSpecialProducts()
    {
        return Session.IsOnline && !IsBusy;
    }

    private bool IsLoadedForCurrentStore()
    {
        return _hasLoaded &&
            string.Equals(_loadedStoreCode, NormalizeStoreCode(Session.StoreCode), StringComparison.OrdinalIgnoreCase);
    }

    private bool CanMoveUp(SellableItemDto? item)
    {
        return item is not null && IsEditMode && !IsBusy && SpecialItems.IndexOf(item) > 0;
    }

    private bool CanMoveDown(SellableItemDto? item)
    {
        var index = item is null ? -1 : SpecialItems.IndexOf(item);
        return index >= 0 && IsEditMode && !IsBusy && index < SpecialItems.Count - 1;
    }

    private bool CanShowPreviousPage()
    {
        return !IsBusy && CurrentPage > 1;
    }

    private bool CanShowNextPage()
    {
        return !IsBusy && CurrentPage < TotalPages;
    }

    private void RefreshCommandStates()
    {
        DownloadCommand.NotifyCanExecuteChanged();
        ToggleEditModeCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        AddSpecialProductCommand.NotifyCanExecuteChanged();
        RemoveSpecialProductCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private void SetStatus(string key, params object[] args)
    {
        StatusMessage = args.Length == 0
            ? T(key)
            : string.Format(_localization.CurrentCulture, T(key), args);
    }

    private void SetStatusText(string message)
    {
        StatusMessage = message;
    }

    private void RaiseLocalizedProperties()
    {
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(BackText));
        OnPropertyChanged(nameof(SearchPlaceholderText));
        OnPropertyChanged(nameof(SearchButtonText));
        OnPropertyChanged(nameof(ClearSearchText));
        OnPropertyChanged(nameof(RefreshText));
        OnPropertyChanged(nameof(DownloadText));
        OnPropertyChanged(nameof(EditModeText));
        OnPropertyChanged(nameof(PreviousPageText));
        OnPropertyChanged(nameof(NextPageText));
        OnPropertyChanged(nameof(PageStatusText));
        OnPropertyChanged(nameof(AddText));
        OnPropertyChanged(nameof(RemoveText));
        OnPropertyChanged(nameof(MoveUpText));
        OnPropertyChanged(nameof(MoveDownText));
        OnPropertyChanged(nameof(TapToAddText));
        OnPropertyChanged(nameof(SearchResultsText));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(NoSearchResultsText));
        OnPropertyChanged(nameof(OnlineStateText));
    }

    private string T(string key)
    {
        return _localization.T(key);
    }

    private static void Log(string message)
    {
        ConsoleLog.Write("SpecialProducts", message);
    }

    private void ApplyDownloadProgress(SpecialProductDownloadProgress progress)
    {
        Log($"operation=download-progress store={progress.StoreCode} stage={progress.Stage} percent={progress.Percent} downloaded={progress.DownloadedCount} total={progress.TotalCount} pages={progress.PageCount} elapsedMs={progress.ElapsedMilliseconds}");
        IsDownloadProgressVisible = true;
        IsDownloadProgressFailed = progress.Stage == SpecialProductDownloadProgressStage.Failed;
        DownloadProgressValue = progress.Percent;

        var titleKey = progress.Stage switch
        {
            SpecialProductDownloadProgressStage.Completed => "specialProducts.download.completed",
            SpecialProductDownloadProgressStage.Failed => "specialProducts.download.failed",
            _ => "specialProducts.download.downloading"
        };
        DownloadProgressText = string.Format(
            _localization.CurrentCulture,
            T(titleKey),
            progress.Percent);

        DownloadProgressDetailText = progress.Stage == SpecialProductDownloadProgressStage.Failed
            ? (progress.ErrorMessage ?? string.Empty)
            : string.Format(
                _localization.CurrentCulture,
                T("specialProducts.download.detail"),
                progress.DownloadedCount,
                progress.TotalCount,
                progress.PageCount,
                progress.UpsertedCount,
                progress.UnmarkedCount,
                FormatElapsed(progress.ElapsedMilliseconds));
    }

    private string FormatElapsed(long elapsedMilliseconds)
    {
        return string.Format(
            _localization.CurrentCulture,
            T("shell.catalogDownload.elapsedSeconds"),
            elapsedMilliseconds / 1000d);
    }

    private static int PreferredLookupRank(SellableItemDto item)
    {
        if (!string.IsNullOrWhiteSpace(item.Barcode) &&
            string.Equals(NormalizeLookupCode(item.LookupCode), NormalizeLookupCode(item.Barcode), StringComparison.Ordinal))
        {
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(item.ItemNumber) &&
            string.Equals(NormalizeLookupCode(item.LookupCode), NormalizeLookupCode(item.ItemNumber), StringComparison.Ordinal))
        {
            return 1;
        }

        return 2;
    }

    private static string NormalizeLookupCode(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static string NormalizeStoreCode(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string NormalizeProductCode(string? value)
    {
        return (value ?? string.Empty).Trim();
    }
}
