using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;

namespace Hbpos.Client.Wpf.Services;

internal sealed class SyncOrchestrator
{
    private readonly IShellSyncCenterService _shellSyncCenterService;
    private readonly IOrderUploadExecutionService _orderUploadExecutionService;
    private readonly ILocalizationService _localization;
    private readonly Action<string>? _setStatusMessage;
    private readonly Action<int>? _onPendingSyncCountChanged;
    private readonly Func<int>? _getPendingSyncCount;
    private readonly Action? _refreshShell;
    private readonly Action<string>? _notifyPropertyChanged;
    private ClientLogOutboxStore? _logOutboxStore;
    private OperationAuditUploadService? _operationAuditUploadService;
    private DeviceAuthorizationState? _deviceAuthorizationState;

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
        DeviceAuthorizationState? deviceAuthorizationState = null)
    {
        _shellSyncCenterService = shellSyncCenterService;
        _orderUploadExecutionService = orderUploadExecutionService;
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
        RetrySyncOrderCommand = new AsyncRelayCommand<SyncQueueListItem?>(RetrySyncOrderAsync, CanRetrySyncOrder);
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

    public async Task RefreshPendingSyncAsync()
    {
        ApplySyncCenterSnapshot(await _shellSyncCenterService.GetSnapshotAsync());
        await RefreshAuditLogsSafelyAsync();
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
    }

    public void ApplySyncCenterSnapshot(ShellSyncCenterSnapshot snapshot)
    {
        PendingUploadCount = snapshot.Overview.PendingCount;
        _notifyPropertyChanged?.Invoke(nameof(PendingUploadCount));
        FailedUploadCount = snapshot.Overview.FailedCount;
        _notifyPropertyChanged?.Invoke(nameof(FailedUploadCount));
        SyncingOrderCount = snapshot.Overview.SyncingCount;
        _notifyPropertyChanged?.Invoke(nameof(SyncingOrderCount));
        LastOrderSyncErrorText = snapshot.Overview.LastError ?? _localization.T("shell.sync.noErrors");
        _notifyPropertyChanged?.Invoke(nameof(LastOrderSyncErrorText));
        SyncCenterOrders.Clear();
        foreach (var item in snapshot.ActiveItems)
        {
            SyncCenterOrders.Add(item);
        }
        _notifyPropertyChanged?.Invoke(nameof(SyncCenterOrders));
        _onPendingSyncCountChanged?.Invoke(snapshot.Overview.PendingCount);
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
        if (item is null)
        {
            return;
        }

        await ExecuteOrderSyncRetryAsync(
            () => _orderUploadExecutionService.ExecuteOneAsync(item.EntityId),
            "shell.sync.retryingOne");
    }

    private async Task RetryAllSyncOrdersAsync()
    {
        await ExecuteOrderSyncRetryAsync(
            () => _orderUploadExecutionService.ExecutePendingAsync(),
            "shell.sync.retryingAll");
    }

    private async Task RetrySelectedSyncOrdersAsync()
    {
        var selected = SyncCenterOrders
            .Where(item => item.CanRetry && item.Selection.IsSelected)
            .Select(item => item.EntityId)
            .ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        await ExecuteOrderSyncRetryAsync(
            () => _orderUploadExecutionService.ExecuteSelectedAsync(selected),
            "shell.sync.retryingSelected");
    }

    private void SelectAllSyncOrders()
    {
        foreach (var item in SyncCenterOrders.Where(item => item.CanRetry))
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

    private async Task ExecuteOrderSyncRetryAsync(
        Func<Task<OrderUploadExecutionResult>> executeAsync,
        string retryingStatusKey)
    {
        if (IsOrderSyncRetrying)
        {
            return;
        }

        IsOrderSyncRetrying = true;
        _notifyPropertyChanged?.Invoke(nameof(IsOrderSyncRetrying));
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
        }
    }

    private bool CanRetrySyncOrder(SyncQueueListItem? item)
    {
        return !IsOrderSyncRetrying &&
            item is not null &&
            item.EntityType.Equals("Order", StringComparison.OrdinalIgnoreCase) &&
            IsRetryableSyncStatus(item.Status);
    }

    private bool CanRetryAllSyncOrders()
    {
        return !IsOrderSyncRetrying && PendingUploadCount + FailedUploadCount > 0;
    }

    private static bool IsRetryableSyncStatus(string status)
    {
        return status.Equals("Pending", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Failed", StringComparison.OrdinalIgnoreCase);
    }
}
