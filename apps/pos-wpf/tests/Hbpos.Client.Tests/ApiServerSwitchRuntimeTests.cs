using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Tests;

public sealed class ApiServerSwitchRuntimeTests
{
    [Fact]
    public async Task Bidirectional_runtime_switch_keeps_local_data_and_wakes_audit_upload_on_each_new_endpoint()
    {
        var localDatabasePath = Path.Combine(Path.GetTempPath(), $"hbpos-runtime-switch-{Guid.NewGuid():N}.db");
        var logDatabasePath = Path.Combine(Path.GetTempPath(), $"hbpos-runtime-switch-logs-{Guid.NewGuid():N}.db");
        const string remoteAddress = "https://remote.example.test/pos-api/";
        const string localAddress = "http://127.0.0.1:5159/";
        var endpointState = new ApiRuntimeEndpointState(remoteAddress);
        var localStore = new LocalSqliteStore(localDatabasePath);
        var schema = new LocalSchemaService(localStore);
        var orders = new LocalOrderRepository(localStore);
        var queue = new SyncQueueRepository(localStore);
        var logStore = new ClientLogOutboxStore(logDatabasePath);
        var deviceAuthorization = new DeviceAuthorizationState();
        deviceAuthorization.Set(new DeviceAuthorizationContext("POS-01", "S001", "HW-01", "secret"));
        var cashierContext = new CashierSessionContext();
        var writer = new ClientLogOutboxWriter(
            logStore,
            deviceAuthorization,
            cashierContext,
            new ClientLogIdentity("runtime-switch", "1.0.0"));
        var capture = new CapturingAuditHandler();
        var timeProvider = new ObservableWaitTimeProvider();
        using var client = new HttpClient(new ApiRuntimeEndpointHandler(endpointState) { InnerHandler = capture })
        {
            BaseAddress = new Uri(remoteAddress)
        };
        using var uploader = new OperationAuditUploadService(
            logStore,
            client,
            timeProvider,
            new OperationAuditUploadOptions(true),
            deviceAuthorization);
        var orderSyncClient = new OrderSyncApiClient(client);
        var runtime = new ApiServerSwitchRuntime(
            new PosCartService(),
            writer,
            uploader,
            endpointState,
            new StubOperationAuthorizationService(),
            cashierContext);
        runtime.ConfigureShell(
            () => ApiServerPaymentSafetyState.Safe,
            _ => Task.CompletedTask,
            _ => { });
        var order = CreateOrder();

        try
        {
            await schema.InitializeAsync();
            await logStore.InitializeAsync(CancellationToken.None);
            await orders.SavePendingOrderAsync(order);
            var expiredRejectedId = Guid.NewGuid();
            var expiredAt = DateTimeOffset.UtcNow.AddDays(-31);
            await logStore.EnqueueAsync(
                ClientLogOutboxKind.OperationAudit,
                expiredRejectedId,
                expiredAt,
                JsonSerializer.Serialize(new { eventId = expiredRejectedId, storeCode = "S001", deviceCode = "POS-01" }),
                expiredAt,
                CancellationToken.None);
            await logStore.ApplyResultsAsync(
                ClientLogOutboxKind.OperationAudit,
                [],
                [new ClientLogRejection(expiredRejectedId, "REJECTED", "expired")],
                expiredAt,
                CancellationToken.None);
            var originalPath = localStore.ActiveDatabasePath;
            var safety = await runtime.GetSafetySnapshotAsync(CancellationToken.None);
            Assert.Null(safety.GetBlockReason());
            Assert.Equal(0, safety.PendingSyncCount);
            Assert.Equal(0, safety.PendingOperationAuditCount);
            await uploader.StartAsync(CancellationToken.None);
            // 中文说明：先观察首次清理和定时器创建，再验证 PostCommit 能从 60 秒等待中唤醒上传器。
            await WaitForNoRejectedOperationEventsAsync(logStore);
            await timeProvider.WaitUntilPeriodicDelayAsync();

            await SwitchAsync(runtime, localAddress);
            var localSyncResponse = await orderSyncClient.SyncAsync(CreateOrderSyncRequest(order));
            var localOrderRequest = await capture.ReadNextAsync();
            var localEventId = Guid.NewGuid();
            await EnqueueAuditAsync(logStore, localEventId);
            await runtime.PostCommitAsync(CancellationToken.None);
            var localAuditRequest = await capture.ReadNextAsync();

            Assert.True(localSyncResponse.Accepted);
            Assert.Equal(new Uri("http://127.0.0.1:5159/api/v1/orders/sync"), localOrderRequest);
            Assert.Equal(new Uri("http://127.0.0.1:5159/api/v1/operation-audits/batch"), localAuditRequest);
            Assert.Equal(originalPath, localStore.ActiveDatabasePath);
            Assert.NotNull(await orders.GetOrderAsync(order.OrderGuid));
            Assert.Equal(order.OrderGuid, Assert.Single(await orders.GetRecentOrdersAsync()).OrderGuid);
            Assert.Equal(1, (await queue.GetOverviewAsync()).PendingCount);

            await SwitchAsync(runtime, remoteAddress);
            var remoteSyncResponse = await orderSyncClient.SyncAsync(CreateOrderSyncRequest(order));
            var remoteOrderRequest = await capture.ReadNextAsync();
            var remoteEventId = Guid.NewGuid();
            await EnqueueAuditAsync(logStore, remoteEventId);
            await runtime.PostCommitAsync(CancellationToken.None);
            var remoteAuditRequest = await capture.ReadNextAsync();

            Assert.True(remoteSyncResponse.Accepted);
            Assert.Equal(new Uri("https://remote.example.test/pos-api/api/v1/orders/sync"), remoteOrderRequest);
            Assert.Equal(new Uri("https://remote.example.test/pos-api/api/v1/operation-audits/batch"), remoteAuditRequest);
            Assert.Equal(originalPath, localStore.ActiveDatabasePath);
            Assert.NotNull(await orders.GetOrderAsync(order.OrderGuid));
            Assert.Equal(order.OrderGuid, Assert.Single(await orders.GetRecentOrdersAsync()).OrderGuid);
            Assert.Equal(1, (await queue.GetOverviewAsync()).PendingCount);
        }
        finally
        {
            await uploader.StopAsync(CancellationToken.None);
            writer.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            DeleteDatabaseFiles(localDatabasePath);
            DeleteDatabaseFiles(logDatabasePath);
        }
    }

    private static async Task SwitchAsync(ApiServerSwitchRuntime runtime, string address)
    {
        var prepared = await runtime.PrepareAsync(address, CancellationToken.None);
        var transition = await runtime.BeginTransitionAsync(address, prepared, CancellationToken.None);
        Assert.Null((await runtime.GetFinalSafetySnapshotAsync(transition, CancellationToken.None)).GetBlockReason());
        Assert.True(runtime.Commit(transition));
    }

    private static Task EnqueueAuditAsync(ClientLogOutboxStore store, Guid eventId)
    {
        var now = DateTimeOffset.UtcNow;
        return store.EnqueueAsync(
            ClientLogOutboxKind.OperationAudit,
            eventId,
            now,
            JsonSerializer.Serialize(new { eventId, storeCode = "S001", deviceCode = "POS-01" }),
            now,
            CancellationToken.None);
    }

    private static async Task WaitForNoRejectedOperationEventsAsync(ClientLogOutboxStore store)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((await store.ReadRejectedAsync(
                    ClientLogOutboxKind.OperationAudit,
                    1,
                    CancellationToken.None)).Count == 0)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("操作审计首次清理周期未在预期时间内完成。");
    }

    private sealed class ObservableWaitTimeProvider : TimeProvider
    {
        private readonly TaskCompletionSource _periodicDelayStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            if (dueTime >= TimeSpan.FromSeconds(60))
            {
                _periodicDelayStarted.TrySetResult();
            }

            return TimeProvider.System.CreateTimer(callback, state, dueTime, period);
        }

        public Task WaitUntilPeriodicDelayAsync() =>
            _periodicDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static LocalOrder CreateOrder() => new(
        Guid.NewGuid(),
        "S001",
        "POS-01",
        "C001",
        "Alice",
        DateTimeOffset.UtcNow,
        1m,
        0m,
        1m,
        [new LocalOrderLine(Guid.NewGuid(), "P001", null, "Tea", "930001", null, 1m, 1m, 0m, 1m, PriceSourceKind.ProductBase)],
        []);

    private static OrderSyncRequest CreateOrderSyncRequest(LocalOrder order) => new(
        order.OrderGuid,
        order.StoreCode,
        order.DeviceCode,
        order.CashierId,
        order.CashierName,
        order.SoldAt,
        order.TotalAmount,
        order.DiscountAmount,
        order.ActualAmount,
        order.Lines.Select(line => new OrderLineSyncDto(
            line.OrderLineGuid,
            line.ProductCode,
            line.ReferenceCode,
            line.DisplayName,
            line.LookupCode,
            line.Quantity,
            line.UnitPrice,
            line.DiscountAmount,
            line.ActualAmount,
            line.PriceSource,
            line.ItemNumber,
            line.Kind,
            line.ReturnSourceKey,
            line.OriginalOrderGuid,
            line.OriginalOrderDetailGuid)).ToArray(),
        []);

    private static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            if (File.Exists(databasePath + suffix))
            {
                File.Delete(databasePath + suffix);
            }
        }
    }

    private sealed class CapturingAuditHandler : HttpMessageHandler
    {
        private readonly Queue<Uri> _requests = [];
        private readonly SemaphoreSlim _signal = new(0);

        public async Task<Uri> ReadNextAsync()
        {
            await _signal.WaitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            lock (_requests)
            {
                return _requests.Dequeue();
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (_requests)
            {
                _requests.Enqueue(request.RequestUri!);
            }

            _signal.Release();
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/v1/orders/sync", StringComparison.Ordinal))
            {
                var orderGuid = document.RootElement.GetProperty("orderGuid").GetGuid();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(ApiResult<OrderSyncResponse>.Ok(
                        new OrderSyncResponse(orderGuid, true, false, "Synced")))
                };
            }

            if (!request.RequestUri.AbsolutePath.EndsWith(
                    "/api/v1/operation-audits/batch",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"未预期的测试请求路径：{request.RequestUri.AbsolutePath}");
            }

            var results = document.RootElement.GetProperty("events")
                .EnumerateArray()
                .Select(item => new { eventId = item.GetProperty("eventId").GetGuid(), status = "accepted" })
                .ToArray();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { results })
            };
        }
    }

    private sealed class StubOperationAuthorizationService : IOperationAuthorizationService
    {
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public event EventHandler? StatusChanged { add { } remove { } }
        public string ScannerPageId => "test";
        public bool IsPromptOpen => false;
        public bool IsBusy => false;
        public string PromptMessage => string.Empty;
        public string StatusMessage => string.Empty;
        public string PermissionCode => string.Empty;
        public string Screen => string.Empty;
        public string Action => string.Empty;
        public IRelayCommand CancelCommand { get; } = new RelayCommand(() => { });
        public Task<OperationAuthorizationScope?> AuthorizeAsync(
            string permissionCode,
            string screen,
            string action,
            PosSessionState session,
            CancellationToken cancellationToken = default) => Task.FromResult<OperationAuthorizationScope?>(null);
        public bool ProcessScannerBarcode(string barcode) => false;
        public void Cancel() { }
        public void RevokeAll() { }
    }
}
