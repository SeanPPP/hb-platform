using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class SyncOrchestratorTests
{
    [Fact]
    public async Task Select_all_orders_selects_only_retryable_items_and_selected_retry_keeps_partial_failure_summary()
    {
        var pending = CreateItem("Pending");
        var failed = CreateItem("Failed");
        var syncing = CreateItem("Syncing");
        var initialSnapshot = new ShellSyncCenterSnapshot(
            new SyncQueueOverview(1, 1, 1, "network"),
            [pending, failed, syncing]);
        var refreshedFailed = new SyncQueueListItem(
            failed.EntityId,
            "Order",
            "Failed",
            failed.CreatedAt,
            DateTimeOffset.UtcNow,
            "still failed",
            failed.Amount);
        var refreshedSnapshot = new ShellSyncCenterSnapshot(
            new SyncQueueOverview(0, 1, 0, "still failed"),
            [refreshedFailed]);
        var snapshotService = new SequenceSyncCenterService(initialSnapshot, refreshedSnapshot);
        var executor = new CapturingOrderExecutor(new OrderUploadExecutionResult(2, 1, 1));
        var status = string.Empty;
        var orchestrator = new SyncOrchestrator(
            snapshotService,
            executor,
            new LocalizationService(),
            setStatusMessage: value => status = value);

        await orchestrator.RefreshPendingSyncAsync();

        orchestrator.SelectAllSyncOrdersCommand.Execute(null);

        Assert.True(pending.Selection.IsSelected);
        Assert.True(failed.Selection.IsSelected);
        Assert.False(syncing.Selection.IsSelected);

        await orchestrator.RetrySelectedSyncOrdersCommand.ExecuteAsync(null);

        Assert.Equal([pending.EntityId, failed.EntityId], executor.SelectedIds);
        Assert.Equal(2, snapshotService.CallCount);
        var refreshed = Assert.Single(orchestrator.SyncCenterOrders);
        Assert.Same(refreshedFailed, refreshed);
        Assert.Equal("Failed", refreshed.Status);
        Assert.False(refreshed.Selection.IsSelected);
        Assert.Equal("Order upload retry completed: 1 succeeded, 1 failed.", status);
    }

    [Fact]
    public async Task Audit_retry_refreshes_selected_rejected_item_as_pending()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-sync-center-audit-{Guid.NewGuid():N}.db");
        var store = new ClientLogOutboxStore(databasePath);
        var eventId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var authorization = new DeviceAuthorizationState();
        authorization.Set(new DeviceAuthorizationContext("POS-01", "S001", "HW-01", "secret"));
        using var client = new HttpClient { BaseAddress = new Uri("https://api.example.test/") };
        using var uploader = new OperationAuditUploadService(
            store,
            client,
            TimeProvider.System,
            new OperationAuditUploadOptions(false),
            authorization);
        try
        {
            await store.InitializeAsync(CancellationToken.None);
            await store.EnqueueAsync(
                ClientLogOutboxKind.OperationAudit,
                eventId,
                now,
                "{\"storeCode\":\"S001\",\"deviceCode\":\"POS-01\"}",
                now,
                CancellationToken.None);
            await store.ApplyResultsAsync(
                ClientLogOutboxKind.OperationAudit,
                [],
                [new ClientLogRejection(eventId, "REJECTED", "old error")],
                now,
                CancellationToken.None);
            var orchestrator = new SyncOrchestrator(
                new StaticSyncCenterService(new ShellSyncCenterSnapshot(
                    new SyncQueueOverview(0, 0, 0, null), [])),
                NoopOrderUploadExecutionService.Instance,
                new LocalizationService(),
                logOutboxStore: store,
                operationAuditUploadService: uploader,
                deviceAuthorizationState: authorization);

            await orchestrator.RefreshPendingSyncAsync();
            Assert.Equal("Rejected", Assert.Single(orchestrator.SyncCenterAuditLogs).Status);
            orchestrator.SyncCenterAuditLogs[0].Selection.IsSelected = true;

            await orchestrator.RetrySelectedAuditLogsCommand.ExecuteAsync(null);

            var refreshed = Assert.Single(orchestrator.SyncCenterAuditLogs);
            Assert.Equal("Pending", refreshed.Status);
            Assert.Null(refreshed.ErrorCode);
            Assert.Null(refreshed.ErrorMessage);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                if (File.Exists(databasePath + suffix))
                {
                    File.Delete(databasePath + suffix);
                }
            }
        }
    }

    [Fact]
    public async Task Empty_order_selection_does_not_execute_or_report_completion()
    {
        var pending = CreateItem("Pending");
        var executor = new CapturingOrderExecutor(new OrderUploadExecutionResult(1, 1, 0));
        var status = string.Empty;
        var orchestrator = new SyncOrchestrator(
            new StaticSyncCenterService(new ShellSyncCenterSnapshot(
                new SyncQueueOverview(1, 0, 0, null),
                [pending])),
            executor,
            new LocalizationService(),
            setStatusMessage: value => status = value);
        await orchestrator.RefreshPendingSyncAsync();

        await orchestrator.RetrySelectedSyncOrdersCommand.ExecuteAsync(null);

        Assert.Equal(0, executor.SelectedCallCount);
        Assert.Equal(string.Empty, status);
    }

    [Fact]
    public async Task Empty_audit_selection_does_not_report_queued_retry()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-sync-center-empty-audit-{Guid.NewGuid():N}.db");
        var store = new ClientLogOutboxStore(databasePath);
        var authorization = new DeviceAuthorizationState();
        authorization.Set(new DeviceAuthorizationContext("POS-01", "S001", "HW-01", "secret"));
        var status = string.Empty;
        try
        {
            await store.InitializeAsync(CancellationToken.None);
            var orchestrator = new SyncOrchestrator(
                new StaticSyncCenterService(new ShellSyncCenterSnapshot(
                    new SyncQueueOverview(0, 0, 0, null), [])),
                NoopOrderUploadExecutionService.Instance,
                new LocalizationService(),
                setStatusMessage: value => status = value,
                logOutboxStore: store,
                deviceAuthorizationState: authorization);
            await orchestrator.RefreshPendingSyncAsync();

            await orchestrator.RetrySelectedAuditLogsCommand.ExecuteAsync(null);

            Assert.Equal(string.Empty, status);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                if (File.Exists(databasePath + suffix))
                {
                    File.Delete(databasePath + suffix);
                }
            }
        }
    }

    [Fact]
    public async Task Audit_read_failure_keeps_order_snapshot_available()
    {
        var blockedParent = Path.GetTempFileName();
        try
        {
            var pending = CreateItem("Pending");
            var authorization = new DeviceAuthorizationState();
            authorization.Set(new DeviceAuthorizationContext("POS-01", "S001", "HW-01", "secret"));
            var orchestrator = new SyncOrchestrator(
                new StaticSyncCenterService(new ShellSyncCenterSnapshot(
                    new SyncQueueOverview(1, 0, 0, null),
                    [pending])),
                NoopOrderUploadExecutionService.Instance,
                new LocalizationService(),
                logOutboxStore: new ClientLogOutboxStore(Path.Combine(blockedParent, "logs.db")),
                deviceAuthorizationState: authorization);

            await orchestrator.RefreshPendingSyncAsync();

            Assert.Same(pending, Assert.Single(orchestrator.SyncCenterOrders));
            Assert.StartsWith("Audit log refresh failed:", orchestrator.LastAuditSyncErrorText, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(blockedParent);
        }
    }

    private static SyncQueueListItem CreateItem(string status) => new(
        Guid.NewGuid(),
        "Order",
        status,
        DateTimeOffset.UtcNow,
        null,
        null,
        1m);

    private sealed class StaticSyncCenterService(ShellSyncCenterSnapshot snapshot) : IShellSyncCenterService
    {
        public Task<ShellSyncCenterSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class SequenceSyncCenterService(params ShellSyncCenterSnapshot[] snapshots) : IShellSyncCenterService
    {
        public int CallCount { get; private set; }

        public Task<ShellSyncCenterSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = Math.Min(CallCount, snapshots.Length - 1);
            CallCount++;
            return Task.FromResult(snapshots[index]);
        }
    }

    private sealed class CapturingOrderExecutor(OrderUploadExecutionResult result) : IOrderUploadExecutionService
    {
        public IReadOnlyList<Guid> SelectedIds { get; private set; } = [];

        public int SelectedCallCount { get; private set; }

        public Task<OrderUploadExecutionResult> ExecuteOneAsync(Guid orderGuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);

        public Task<OrderUploadExecutionResult> ExecutePendingAsync(int batchSize = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);

        public Task<OrderUploadExecutionResult> ExecuteSelectedAsync(
            IReadOnlyCollection<Guid> orderGuids,
            CancellationToken cancellationToken = default)
        {
            SelectedCallCount++;
            SelectedIds = orderGuids.ToArray();
            return Task.FromResult(result);
        }
    }
}
