using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;

namespace Hbpos.Client.Wpf.Services;

internal sealed class SyncOrchestrator
{
    private const string OrderEntityType = "Order";
    private const string LinklySettlementEntityType = "LinklySettlement";

    private readonly IShellSyncCenterService _shellSyncCenterService;
    private readonly IOrderUploadExecutionService _orderUploadExecutionService;
    private readonly ILinklySettlementUploadQueueReader? _linklySettlementUploadQueueReader;
    private readonly ILinklySettlementUploadExecutionService? _linklySettlementUploadExecutionService;
    private readonly ILocalizationService _localization;
    private readonly Action<string>? _setStatusMessage;
    private readonly Action<int>? _onPendingSyncCountChanged;
    private readonly Func<int>? _getPendingSyncCount;
    private readonly Action? _refreshShell;
    private readonly Action<string>? _notifyPropertyChanged;
    private ClientLogOutboxStore? _logOutboxStore;
    private OperationAuditUploadService? _operationAuditUploadService;
    private DeviceAuthorizationState? _deviceAuthorizationState;
    private SyncQueueOverview _orderOverview = new(0, 0, 0, null);
    private LinklySettlementUploadOverview _settlementOverview = new(0, 0, 0, null);
    private IReadOnlyList<LinklySettlementUploadQueueItem> _settlementQueueItems = [];
    private string? _settlementLoadFailureMessage;
    private bool _isAutoSyncRetrying;

    public SyncOrchestrator(
        IShellSyncCenterService shellSyncCenterService,
        IOrderUploadExecutionService orderUploadExecutionService,
        ILocalizationService localization,
        Action<string>? setStatusMessage = null,
        Action<int>? onPendingSyncCountChanged = null,
        Func<int>? getPendingSyncCount = null,
        Action? refreshShell = null,
        Action<string>? notifyPropertyChanged = null,
        ClientLogOutboxStore? logOutboxStore = null,
        OperationAuditUploadService? operationAuditUploadService = null,
        DeviceAuthorizationState? deviceAuthorizationState = null,
        ILinklySettlementUploadQueueReader? linklySettlementUploadQueueReader = null,
        ILinklySettlementUploadExecutionService? linklySettlementUploadExecutionService = null)
    {
        _shellSyncCenterService = shellSyncCenterService;
        _orderUploadExecutionService = orderUploadExecutionService;
        _linklySettlementUploadQueueReader = linklySettlementUploadQueueReader;
        _linklySettlementUploadExecutionService = linklySettlementUploadExecutionService;
        _localization = localization;
        _setStatusMessage = setStatusMessage;
        _onPendingSyncCountChanged = onPendingSyncCountChanged;
        _getPendingSyncCount = getPendingSyncCount;
        _refreshShell = refreshShell;
        _notifyPropertyChanged = notifyPropertyChanged;
        _logOutboxStore = logOutboxStore;
        _operationAuditUploadService = operationAuditUploadService;
        _deviceAuthorizationState = deviceAuthorizationState;

        ToggleSyncCenterCommand = new AsyncRelayCommand(ToggleSyncCenterAsync);
        RetrySyncOrderCommand = new AsyncRelayCommand<SyncQueueListItem?>(RetrySyncOrderAsync, CanRetrySyncEntity);
        RetryAllSyncOrdersCommand = new AsyncRelayCommand(RetryAllSyncOrdersAsync, CanRetryAllSyncOrders);
        RetrySelectedSyncOrdersCommand = new AsyncRelayCommand(RetrySelectedSyncOrdersAsync);
        SelectAllSyncOrdersCommand = new RelayCommand(SelectAllSyncOrders);
        RetrySelectedAuditLogsCommand = new AsyncRelayCommand(RetrySelectedAuditLogsAsync);
        SelectAllAuditLogsCommand = new RelayCommand(SelectAllAuditLogs);
    }

    // ---- State properties ----

    public ObservableCollection<SyncQueueListItem> SyncCenterOrders { get; } = [];

    public ObservableCollection<OperationAuditQueueListItem> SyncCenterAuditLogs { get; } = [];

    public bool IsSyncCenterExpanded { get; set; }

    public string SyncCenterDetailTitle { get; set; } = string.Empty;

    public string LastOrderSyncErrorText { get; set; } = string.Empty;

    public string LastAuditSyncErrorText { get; set; } = string.Empty;

    public string PendingSyncText { get; set; } = string.Empty;

    public string OrderSyncStatusText { get; set; } = string.Empty;

    public int PendingUploadCount { get; set; }

    public int FailedUploadCount { get; set; }

    public int SyncingOrderCount { get; set; }

    public bool IsOrderSyncRetrying { get; set; }

    // ---- Commands ----

    public IAsyncRelayCommand ToggleSyncCenterCommand { get; }

    public IAsyncRelayCommand<SyncQueueListItem?> RetrySyncOrderCommand { get; }

    public IAsyncRelayCommand RetryAllSyncOrdersCommand { get; }

    public IAsyncRelayCommand RetrySelectedSyncOrdersCommand { get; }

    public IRelayCommand SelectAllSyncOrdersCommand { get; }

    public IAsyncRelayCommand RetrySelectedAuditLogsCommand { get; }

    public IRelayCommand SelectAllAuditLogsCommand { get; }

    // ---- Public methods ----

    public async Task RefreshPendingSyncAsync(CancellationToken cancellationToken = default)
    {
        var orderSnapshot = await _shellSyncCenterService.GetSnapshotAsync(cancellationToken);
        await RefreshSettlementUploadsSafelyAsync(cancellationToken);
        ApplySyncCenterSnapshot(orderSnapshot);
        await RefreshAuditLogsSafelyAsync();
    }

    public async Task TryAutoRetryPendingAsync(CancellationToken cancellationToken)
    {
        if (_isAutoSyncRetrying || IsOrderSyncRetrying)
        {
            return;
        }

        _isAutoSyncRetrying = true;
        RefreshSyncRetryCommandStates();
        try
        {
            await RefreshPendingSyncAsync(cancellationToken);
            if (PendingUploadCount + FailedUploadCount == 0)
            {
                return;
            }

            ConsoleLog.Write("UploadSync", "auto retry pending start");
            var result = await ExecutePendingUploadsAsync(cancellationToken);
            await RefreshPendingSyncAsync(cancellationToken);
            ConsoleLog.Write(
                "UploadSync",
                $"auto retry pending completed attempted={result.AttemptedCount} uploaded={result.UploadedCount} failed={result.FailedCount}");
        }
        catch (OperationCanceledException)
        {
            ConsoleLog.Write("UploadSync", "auto retry pending canceled");
            throw;
        }
        catch (Exception ex)
        {
            ConsoleLog.Write("UploadSync", $"auto retry pending failed error={ex.GetType().Name} message={ex.Message}");
            try
            {
                await RefreshPendingSyncAsync();
            }
            catch (Exception refreshEx) when (refreshEx is not OperationCanceledException)
            {
                ConsoleLog.Write(
                    "UploadSync",
                    $"auto retry pending refresh failed error={refreshEx.GetType().Name} message={refreshEx.Message}");
            }
        }
        finally
        {
            _isAutoSyncRetrying = false;
            RefreshSyncRetryCommandStates();
        }
    }

    public void ConfigureAudit(
        ClientLogOutboxStore logOutboxStore,
        OperationAuditUploadService operationAuditUploadService,
        DeviceAuthorizationState deviceAuthorizationState)
    {
        _logOutboxStore = logOutboxStore;
        _operationAuditUploadService = operationAuditUploadService;
        _deviceAuthorizationState = deviceAuthorizationState;
    }

    public void RefreshLocalizedText()
    {
        PendingSyncText = string.Format(
            _localization.CurrentCulture,
            _localization.T("pos.status.pendingSync"),
            GetPendingSyncCount());
        _notifyPropertyChanged?.Invoke(nameof(PendingSyncText));
        OrderSyncStatusText = string.Format(
            _localization.CurrentCulture,
            _localization.T("shell.sync.orderStatus"),
            PendingUploadCount,
            FailedUploadCount,
            SyncingOrderCount);
        _notifyPropertyChanged?.Invoke(nameof(OrderSyncStatusText));
        SyncCenterDetailTitle = string.Format(
            _localization.CurrentCulture,
            _localization.T("shell.sync.detailTitle"),
            SyncCenterOrders.Count);
        _notifyPropertyChanged?.Invoke(nameof(SyncCenterDetailTitle));
        LastOrderSyncErrorText = BuildLastUploadErrorText();
        _notifyPropertyChanged?.Invoke(nameof(LastOrderSyncErrorText));
    }

    public void ApplySyncCenterSnapshot(ShellSyncCenterSnapshot snapshot)
    {
        _orderOverview = snapshot.Overview;
        PendingUploadCount = snapshot.Overview.PendingCount + _settlementOverview.PendingCount;
        _notifyPropertyChanged?.Invoke(nameof(PendingUploadCount));
        FailedUploadCount = snapshot.Overview.FailedCount + _settlementOverview.FailedCount;
        _notifyPropertyChanged?.Invoke(nameof(FailedUploadCount));
        SyncingOrderCount = snapshot.Overview.SyncingCount + _settlementOverview.UploadingCount;
        _notifyPropertyChanged?.Invoke(nameof(SyncingOrderCount));
        LastOrderSyncErrorText = BuildLastUploadErrorText();
        _notifyPropertyChanged?.Invoke(nameof(LastOrderSyncErrorText));
        SyncCenterOrders.Clear();
        foreach (var item in snapshot.ActiveItems)
        {
            SyncCenterOrders.Add(item);
        }
        foreach (var item in _settlementQueueItems.Select(MapSettlementQueueItem))
        {
            SyncCenterOrders.Add(item);
        }
        _notifyPropertyChanged?.Invoke(nameof(SyncCenterOrders));
        _onPendingSyncCountChanged?.Invoke(PendingUploadCount);
        RefreshSyncRetryCommandStates();
        _refreshShell?.Invoke();
    }

    public void RefreshSyncRetryCommandStates()
    {
        RetrySyncOrderCommand.NotifyCanExecuteChanged();
        RetryAllSyncOrdersCommand.NotifyCanExecuteChanged();
    }

    // ---- Private helpers ----

    private int GetPendingSyncCount()
    {
        return _getPendingSyncCount?.Invoke() ?? (PendingUploadCount + FailedUploadCount);
    }

    private async Task ToggleSyncCenterAsync()
    {
        if (!IsSyncCenterExpanded)
        {
            await RefreshPendingSyncAsync();
        }

        IsSyncCenterExpanded = !IsSyncCenterExpanded;
        _notifyPropertyChanged?.Invoke(nameof(IsSyncCenterExpanded));
    }

    private async Task RetrySyncOrderAsync(SyncQueueListItem? item)
    {
        if (!CanRetrySyncEntity(item))
        {
            return;
        }

        await ExecuteUploadSyncRetryAsync(
            () => ExecuteOneUploadAsync(item!, CancellationToken.None),
            "shell.sync.retryingOne");
    }

    private async Task RetryAllSyncOrdersAsync()
    {
        await ExecuteUploadSyncRetryAsync(
            () => ExecuteAllUploadsAsync(CancellationToken.None),
            "shell.sync.retryingAll");
    }

    private async Task RetrySelectedSyncOrdersAsync()
    {
        var selected = SyncCenterOrders
            .Where(item => item.Selection.IsSelected && CanRetrySyncEntity(item))
            .ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        await ExecuteUploadSyncRetryAsync(
            () => ExecuteSelectedUploadsAsync(selected, CancellationToken.None),
            "shell.sync.retryingSelected");
    }

    private void SelectAllSyncOrders()
    {
        foreach (var item in SyncCenterOrders.Where(CanRetrySyncEntity))
        {
            item.Selection.IsSelected = true;
        }
    }

    private async Task RefreshAuditLogsAsync()
    {
        var scope = _deviceAuthorizationState?.Current;
        if (_logOutboxStore is null || scope is null)
        {
            SyncCenterAuditLogs.Clear();
            _notifyPropertyChanged?.Invoke(nameof(SyncCenterAuditLogs));
            return;
        }

        var records = await _logOutboxStore.ReadOperationForScopeAsync(
            scope.StoreCode,
            scope.DeviceCode,
            200,
            CancellationToken.None);
        SyncCenterAuditLogs.Clear();
        foreach (var record in records)
        {
            SyncCenterAuditLogs.Add(new OperationAuditQueueListItem(
                record.EventId,
                record.State,
                record.OccurredAtUtc,
                record.AttemptCount,
                record.NextAttemptAtUtc,
                record.LastErrorCode,
                record.LastErrorMessage));
        }

        _notifyPropertyChanged?.Invoke(nameof(SyncCenterAuditLogs));
    }

    private async Task RefreshAuditLogsSafelyAsync()
    {
        try
        {
            await RefreshAuditLogsAsync();
            LastAuditSyncErrorText = string.Empty;
        }
        catch (Exception ex)
        {
            // 审计 outbox 故障不能阻止订单页签刷新或覆盖订单重试结果。
            LastAuditSyncErrorText = string.Format(
                _localization.CurrentCulture,
                _localization.T("shell.sync.auditLoadFailed"),
                ex.Message);
        }

        _notifyPropertyChanged?.Invoke(nameof(LastAuditSyncErrorText));
    }

    private async Task RefreshSettlementUploadsSafelyAsync(CancellationToken cancellationToken)
    {
        if (_linklySettlementUploadQueueReader is null)
        {
            _settlementOverview = new LinklySettlementUploadOverview(0, 0, 0, null);
            _settlementQueueItems = [];
            _settlementLoadFailureMessage = null;
            return;
        }

        try
        {
            var overviewTask = _linklySettlementUploadQueueReader.GetOverviewAsync(cancellationToken);
            var itemsTask = _linklySettlementUploadQueueReader.GetActiveItemsAsync(cancellationToken: cancellationToken);
            await Task.WhenAll(overviewTask, itemsTask);
            _settlementOverview = await overviewTask;
            _settlementQueueItems = await itemsTask;
            _settlementLoadFailureMessage = null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 结算上传队列独立于订单快照，读取失败时保留订单同步中心可用。
            _settlementOverview = new LinklySettlementUploadOverview(0, 0, 0, null);
            _settlementQueueItems = [];
            _settlementLoadFailureMessage = ex.Message;
        }
    }

    private void SelectAllAuditLogs()
    {
        foreach (var item in SyncCenterAuditLogs)
        {
            item.Selection.IsSelected = true;
        }
    }

    private async Task RetrySelectedAuditLogsAsync()
    {
        if (_logOutboxStore is null)
        {
            return;
        }

        var selected = SyncCenterAuditLogs
            .Where(item => item.Selection.IsSelected)
            .Select(item => item.EventId)
            .ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        var reset = await _logOutboxStore.ResetOperationForRetryAsync(
            selected,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        if (reset > 0)
        {
            _operationAuditUploadService?.RequestUpload();
        }

        await RefreshAuditLogsSafelyAsync();
        _setStatusMessage?.Invoke(string.Format(
            _localization.CurrentCulture,
            _localization.T("shell.sync.auditRetryQueued"),
            reset));
    }

    private async Task ExecuteUploadSyncRetryAsync(
        Func<Task<UploadRetrySummary>> executeAsync,
        string retryingStatusKey)
    {
        if (_isAutoSyncRetrying || IsOrderSyncRetrying)
        {
            return;
        }

        IsOrderSyncRetrying = true;
        _notifyPropertyChanged?.Invoke(nameof(IsOrderSyncRetrying));
        RefreshSyncRetryCommandStates();
        _setStatusMessage?.Invoke(_localization.T(retryingStatusKey));
        try
        {
            var result = await executeAsync();
            await RefreshPendingSyncAsync();
            _setStatusMessage?.Invoke(string.Format(
                _localization.CurrentCulture,
                _localization.T("shell.sync.retryCompleted"),
                result.UploadedCount,
                result.FailedCount));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await RefreshPendingSyncAsync();
            _setStatusMessage?.Invoke(string.Format(
                _localization.CurrentCulture,
                _localization.T("shell.sync.retryFailed"),
                ex.Message));
        }
        finally
        {
            IsOrderSyncRetrying = false;
            _notifyPropertyChanged?.Invoke(nameof(IsOrderSyncRetrying));
            RefreshSyncRetryCommandStates();
        }
    }

    private bool CanRetrySyncEntity(SyncQueueListItem? item)
    {
        if (_isAutoSyncRetrying || IsOrderSyncRetrying || item is null || !item.CanRetry)
        {
            return false;
        }

        return item.EntityType.Equals(OrderEntityType, StringComparison.OrdinalIgnoreCase) ||
            (item.EntityType.Equals(LinklySettlementEntityType, StringComparison.OrdinalIgnoreCase) &&
             _linklySettlementUploadExecutionService is not null);
    }

    private bool CanRetryAllSyncOrders()
    {
        return !_isAutoSyncRetrying &&
            !IsOrderSyncRetrying &&
            SyncCenterOrders.Any(CanRetrySyncEntity);
    }

    private async Task<UploadRetrySummary> ExecuteOneUploadAsync(
        SyncQueueListItem item,
        CancellationToken cancellationToken)
    {
        if (item.EntityType.Equals(OrderEntityType, StringComparison.OrdinalIgnoreCase))
        {
            return ToSummary(await _orderUploadExecutionService.ExecuteOneAsync(item.EntityId, cancellationToken));
        }

        if (item.EntityType.Equals(LinklySettlementEntityType, StringComparison.OrdinalIgnoreCase) &&
            _linklySettlementUploadExecutionService is not null)
        {
            return ToSummary(await _linklySettlementUploadExecutionService.ExecuteOneAsync(item.EntityId, cancellationToken));
        }

        return UploadRetrySummary.Empty;
    }

    private async Task<UploadRetrySummary> ExecutePendingUploadsAsync(CancellationToken cancellationToken)
    {
        var tasks = new List<Task<UploadRetrySummary>>
        {
            ExecuteOrderPendingAsync(cancellationToken)
        };
        if (_linklySettlementUploadExecutionService is not null)
        {
            tasks.Add(ExecuteSettlementPendingAsync(cancellationToken));
        }

        return CombineRetryResults(await Task.WhenAll(tasks));
    }

    private async Task<UploadRetrySummary> ExecuteAllUploadsAsync(CancellationToken cancellationToken)
    {
        var tasks = new List<Task<UploadRetrySummary>>
        {
            ExecuteOrderPendingAsync(cancellationToken)
        };
        if (_linklySettlementUploadQueueReader is not null &&
            _linklySettlementUploadExecutionService is not null)
        {
            var settlementIds = (await _linklySettlementUploadQueueReader.GetActiveItemsAsync(
                    int.MaxValue,
                    cancellationToken))
                .Where(item => item.Status is
                    LocalLinklySettlementUploadStatus.Pending or
                    LocalLinklySettlementUploadStatus.Rejected)
                .Select(item => item.SettlementGuid)
                .Distinct()
                .ToArray();
            if (settlementIds.Length > 0)
            {
                // 手动“全部重试”覆盖整个独立结算队列，并忽略后台退避时间。
                tasks.Add(ExecuteSelectedSettlementsAsync(settlementIds, cancellationToken));
            }
        }

        return CombineRetryResults(await Task.WhenAll(tasks));
    }

    private async Task<UploadRetrySummary> ExecuteSelectedUploadsAsync(
        IReadOnlyCollection<SyncQueueListItem> selectedItems,
        CancellationToken cancellationToken)
    {
        var tasks = new List<Task<UploadRetrySummary>>();
        var orderIds = selectedItems
            .Where(item => item.EntityType.Equals(OrderEntityType, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.EntityId)
            .Distinct()
            .ToArray();
        if (orderIds.Length > 0)
        {
            tasks.Add(ExecuteSelectedOrdersAsync(orderIds, cancellationToken));
        }

        var settlementIds = selectedItems
            .Where(item => item.EntityType.Equals(LinklySettlementEntityType, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.EntityId)
            .Distinct()
            .ToArray();
        if (settlementIds.Length > 0 && _linklySettlementUploadExecutionService is not null)
        {
            tasks.Add(ExecuteSelectedSettlementsAsync(settlementIds, cancellationToken));
        }

        return tasks.Count == 0
            ? UploadRetrySummary.Empty
            : CombineRetryResults(await Task.WhenAll(tasks));
    }

    private async Task<UploadRetrySummary> ExecuteOrderPendingAsync(CancellationToken cancellationToken) =>
        ToSummary(await _orderUploadExecutionService.ExecutePendingAsync(cancellationToken: cancellationToken));

    private async Task<UploadRetrySummary> ExecuteSettlementPendingAsync(CancellationToken cancellationToken) =>
        ToSummary(await _linklySettlementUploadExecutionService!.ExecutePendingAsync(cancellationToken: cancellationToken));

    private async Task<UploadRetrySummary> ExecuteSelectedOrdersAsync(
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken) =>
        ToSummary(await _orderUploadExecutionService.ExecuteSelectedAsync(orderIds, cancellationToken));

    private async Task<UploadRetrySummary> ExecuteSelectedSettlementsAsync(
        IReadOnlyCollection<Guid> settlementIds,
        CancellationToken cancellationToken)
    {
        var results = new List<UploadRetrySummary>(settlementIds.Count);
        foreach (var settlementId in settlementIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(ToSummary(await _linklySettlementUploadExecutionService!.ExecuteOneAsync(
                settlementId,
                cancellationToken)));
        }

        return CombineRetryResults(results);
    }

    private string BuildLastUploadErrorText()
    {
        var errors = new List<string>();
        if (!string.IsNullOrWhiteSpace(_orderOverview.LastError))
        {
            errors.Add(_orderOverview.LastError);
        }

        if (!string.IsNullOrWhiteSpace(_settlementOverview.LastError))
        {
            errors.Add(string.Format(
                _localization.CurrentCulture,
                _localization.T("shell.sync.settlementError"),
                _settlementOverview.LastError));
        }

        if (!string.IsNullOrWhiteSpace(_settlementLoadFailureMessage))
        {
            errors.Add(string.Format(
                _localization.CurrentCulture,
                _localization.T("shell.sync.settlementLoadFailed"),
                _settlementLoadFailureMessage));
        }

        return errors.Count == 0
            ? _localization.T("shell.sync.noErrors")
            : string.Join(Environment.NewLine, errors.Distinct(StringComparer.Ordinal));
    }

    private static SyncQueueListItem MapSettlementQueueItem(LinklySettlementUploadQueueItem item)
    {
        var status = item.Status switch
        {
            LocalLinklySettlementUploadStatus.Uploading => "Syncing",
            LocalLinklySettlementUploadStatus.Rejected => "Failed",
            _ => item.Status.ToString()
        };
        var uploadError = !string.IsNullOrWhiteSpace(item.ErrorMessage)
            ? item.ErrorMessage
            : item.ErrorCode;
        var settlementDetails = string.Join(
            " | ",
            new[] { item.ConnectionMode, item.SettlementStatus.ToString(), item.ProviderSubmissionState.ToString() }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        var errorMessage = string.IsNullOrWhiteSpace(uploadError)
            ? settlementDetails
            : $"{settlementDetails} - {uploadError}";

        return new SyncQueueListItem(
            item.SettlementGuid,
            LinklySettlementEntityType,
            status,
            item.CreatedAt,
            item.LastTriedAt,
            errorMessage,
            Amount: null);
    }

    private static UploadRetrySummary ToSummary(OrderUploadExecutionResult result) =>
        new(result.AttemptedCount, result.UploadedCount, result.FailedCount);

    private static UploadRetrySummary ToSummary(LinklySettlementUploadExecutionResult result) =>
        new(result.AttemptedCount, result.UploadedCount, result.FailedCount + result.DeferredCount);

    private static UploadRetrySummary CombineRetryResults(IEnumerable<UploadRetrySummary> results)
    {
        var attempted = 0;
        var uploaded = 0;
        var failed = 0;
        foreach (var result in results)
        {
            attempted += result.AttemptedCount;
            uploaded += result.UploadedCount;
            failed += result.FailedCount;
        }

        return new UploadRetrySummary(attempted, uploaded, failed);
    }

    private sealed record UploadRetrySummary(int AttemptedCount, int UploadedCount, int FailedCount)
    {
        public static UploadRetrySummary Empty { get; } = new(0, 0, 0);
    }
}
