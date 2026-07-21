using System.Net;
using System.Net.Http.Json;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Tests;

public sealed class OrderUploadServiceTests
{
    [Fact]
    public async Task ExecuteSelectedAsync_preserves_order_deduplicates_and_summarizes_failures()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var uploader = new RecordingOrderUploadService(second);
        var executor = new OrderUploadExecutionService(uploader, new StubOrderUploadRepository());

        var result = await executor.ExecuteSelectedAsync([first, second, first, Guid.Empty]);

        Assert.Equal([first, second], uploader.Attempts);
        Assert.Equal(new OrderUploadExecutionResult(2, 1, 1), result);
    }

    [Fact]
    public async Task ExecuteSelectedAsync_with_empty_selection_does_not_upload()
    {
        var uploader = new RecordingOrderUploadService(Guid.Empty);
        var executor = new OrderUploadExecutionService(uploader, new StubOrderUploadRepository());

        var result = await executor.ExecuteSelectedAsync([]);

        Assert.Empty(uploader.Attempts);
        Assert.Equal(new OrderUploadExecutionResult(0, 0, 0), result);
    }

    [Fact]
    public async Task ExecuteSelectedAsync_queues_remaining_orders_until_inflight_endpoint_switch_commits()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-order-upload-switch-{Guid.NewGuid():N}.db");
        const string oldAddress = "https://old.example.test/pos-api/";
        const string newAddress = "https://new.example.test/pos-api/";
        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var orders = new LocalOrderRepository(store);
            var uploadRepository = new LocalOrderUploadRepository(store);
            var first = CreateLocalOrder();
            var alreadySynced = CreateLocalOrder();
            await schema.InitializeAsync();
            await orders.SavePendingOrderAsync(first);
            await orders.SavePendingOrderAsync(alreadySynced);
            await uploadRepository.MarkSyncedAsync(alreadySynced.OrderGuid);

            var endpointState = new ApiRuntimeEndpointState(oldAddress);
            var handler = new EndpointSwitchOrderSyncHandler();
            using var httpClient = new HttpClient(new ApiRuntimeEndpointHandler(endpointState)
            {
                InnerHandler = handler
            })
            {
                BaseAddress = new Uri(oldAddress)
            };
            var executor = new OrderUploadExecutionService(
                new OrderUploadService(orders, new OrderSyncApiClient(httpClient), uploadRepository),
                uploadRepository);

            var execution = executor.ExecuteSelectedAsync([first.OrderGuid, alreadySynced.OrderGuid]);
            await handler.FirstRequestStarted.WaitAsync(TimeSpan.FromSeconds(2));
            var transition = await endpointState.BeginTransitionAsync(newAddress, CancellationToken.None);
            var interrupted = await execution.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(new OrderUploadExecutionResult(2, 0, 2), interrupted);
            Assert.Equal([new Uri($"{oldAddress}api/v1/orders/sync")], handler.RequestUris);
            Assert.All(await orders.GetRecentOrdersAsync(), item => Assert.Equal("Pending", item.SyncStatus));

            endpointState.Commit(transition);
            var resumed = await executor.ExecutePendingAsync();

            Assert.Equal(new OrderUploadExecutionResult(2, 2, 0), resumed);
            Assert.Equal(
                [
                    new Uri($"{oldAddress}api/v1/orders/sync"),
                    new Uri($"{newAddress}api/v1/orders/sync"),
                    new Uri($"{newAddress}api/v1/orders/sync")
                ],
                handler.RequestUris);
            Assert.Equal(
                "Synced",
                (await orders.GetRecentOrdersAsync())
                    .Single(item => item.OrderGuid == alreadySynced.OrderGuid)
                    .SyncStatus);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task ExecuteSelectedAsync_stops_immediately_when_caller_cancels()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var uploader = new CallerCancellationOrderUploadService();
        var executor = new OrderUploadExecutionService(uploader, new StubOrderUploadRepository());
        using var cancellation = new CancellationTokenSource();

        var execution = executor.ExecuteSelectedAsync([first, second], cancellation.Token);
        await uploader.FirstRequestStarted.WaitAsync(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            execution.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal([first], uploader.Attempts);
    }

    [Fact]
    public async Task Execution_service_serializes_cross_entry_point_uploads()
    {
        var orderGuid = Guid.NewGuid();
        var uploader = new BlockingOrderUploadService();
        var executor = new OrderUploadExecutionService(uploader, new StubOrderUploadRepository());

        var automatic = executor.ExecuteOneAsync(orderGuid);
        await uploader.FirstRequestStarted.WaitAsync(TimeSpan.FromSeconds(2));
        var manual = executor.ExecuteSelectedAsync([orderGuid]);
        await Task.Yield();

        Assert.Single(uploader.Attempts);
        Assert.Equal(1, uploader.MaximumConcurrency);

        uploader.ReleaseFirstRequest();
        await Task.WhenAll(automatic, manual).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([orderGuid, orderGuid], uploader.Attempts);
        Assert.Equal(1, uploader.MaximumConcurrency);
    }

    [Fact]
    public async Task Order_upload_execution_service_marks_order_synced_when_api_accepts()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-order-upload-{Guid.NewGuid():N}.db");

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var orders = new LocalOrderRepository(store);
            var uploadRepository = new LocalOrderUploadRepository(store);
            var syncQueue = new SyncQueueRepository(store);
            var order = CreateLocalOrder();

            await schema.InitializeAsync();
            await orders.SavePendingOrderAsync(order);

            var uploadService = new OrderUploadService(
                orders,
                new StubOrderSyncApiClient(new OrderSyncResponse(order.OrderGuid, true, false, "Synced")),
                uploadRepository);
            var executor = new OrderUploadExecutionService(uploadService, uploadRepository);

            var result = await executor.ExecutePendingAsync();
            var summary = Assert.Single(await orders.GetRecentOrdersAsync());
            var activeItems = await syncQueue.GetActiveItemsAsync();

            Assert.Equal(1, result.AttemptedCount);
            Assert.Equal(1, result.UploadedCount);
            Assert.Equal(0, result.FailedCount);
            Assert.Equal("Synced", summary.SyncStatus);
            Assert.Empty(activeItems);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task Order_upload_execution_service_marks_order_failed_when_api_throws()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-order-upload-failed-{Guid.NewGuid():N}.db");

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var orders = new LocalOrderRepository(store);
            var uploadRepository = new LocalOrderUploadRepository(store);
            var syncQueue = new SyncQueueRepository(store);
            var order = CreateLocalOrder();

            await schema.InitializeAsync();
            await orders.SavePendingOrderAsync(order);

            var uploadService = new OrderUploadService(
                orders,
                new ThrowingOrderSyncApiClient("network down"),
                uploadRepository);
            var executor = new OrderUploadExecutionService(uploadService, uploadRepository);

            var result = await executor.ExecutePendingAsync();
            var summary = Assert.Single(await orders.GetRecentOrdersAsync());
            var activeItem = Assert.Single(await syncQueue.GetActiveItemsAsync());

            Assert.Equal(1, result.AttemptedCount);
            Assert.Equal(0, result.UploadedCount);
            Assert.Equal(1, result.FailedCount);
            Assert.Equal("Failed", summary.SyncStatus);
            Assert.Equal("Failed", activeItem.Status);
            Assert.Contains("network down", activeItem.ErrorMessage);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task Order_upload_execution_service_execute_one_marks_order_synced_when_api_accepts()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-order-upload-one-{Guid.NewGuid():N}.db");

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var orders = new LocalOrderRepository(store);
            var uploadRepository = new LocalOrderUploadRepository(store);
            var syncQueue = new SyncQueueRepository(store);
            var order = CreateLocalOrder();

            await schema.InitializeAsync();
            await orders.SavePendingOrderAsync(order);

            var uploadService = new OrderUploadService(
                orders,
                new StubOrderSyncApiClient(new OrderSyncResponse(order.OrderGuid, true, false, "Synced")),
                uploadRepository);
            var executor = new OrderUploadExecutionService(uploadService, uploadRepository);

            var result = await executor.ExecuteOneAsync(order.OrderGuid);
            var summary = Assert.Single(await orders.GetRecentOrdersAsync());
            var activeItems = await syncQueue.GetActiveItemsAsync();

            Assert.Equal(1, result.AttemptedCount);
            Assert.Equal(1, result.UploadedCount);
            Assert.Equal(0, result.FailedCount);
            Assert.Equal("Synced", summary.SyncStatus);
            Assert.Empty(activeItems);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task Order_upload_execution_service_execute_one_returns_failed_count_when_api_throws()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-order-upload-one-failed-{Guid.NewGuid():N}.db");

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var orders = new LocalOrderRepository(store);
            var uploadRepository = new LocalOrderUploadRepository(store);
            var syncQueue = new SyncQueueRepository(store);
            var order = CreateLocalOrder();

            await schema.InitializeAsync();
            await orders.SavePendingOrderAsync(order);

            var uploadService = new OrderUploadService(
                orders,
                new ThrowingOrderSyncApiClient("network down"),
                uploadRepository);
            var executor = new OrderUploadExecutionService(uploadService, uploadRepository);

            var result = await executor.ExecuteOneAsync(order.OrderGuid);
            var summary = Assert.Single(await orders.GetRecentOrdersAsync());
            var activeItem = Assert.Single(await syncQueue.GetActiveItemsAsync());

            Assert.Equal(1, result.AttemptedCount);
            Assert.Equal(0, result.UploadedCount);
            Assert.Equal(1, result.FailedCount);
            Assert.Equal("Failed", summary.SyncStatus);
            Assert.Equal("Failed", activeItem.Status);
            Assert.Contains("network down", activeItem.ErrorMessage);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task Local_order_repository_roundtrips_card_transactions()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-card-transaction-{Guid.NewGuid():N}.db");

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var orders = new LocalOrderRepository(store);
            var order = CreateLocalOrder();

            await schema.InitializeAsync();
            await orders.SavePendingOrderAsync(order);

            var saved = await orders.GetOrderAsync(order.OrderGuid);

            var payment = Assert.Single(saved!.Payments);
            var transaction = Assert.Single(payment.CardTransactions!);
            Assert.Equal("ANZ", transaction.Processor);
            Assert.Equal("TXN-1", transaction.TxnRef);
            Assert.Equal("****1234", transaction.MaskedCardNumber);
            Assert.Equal("merchant receipt", transaction.ReceiptText);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task UploadOrderAsync_sends_card_transactions_to_sync_api()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-card-upload-{Guid.NewGuid():N}.db");

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var orders = new LocalOrderRepository(store);
            var uploadRepository = new LocalOrderUploadRepository(store);
            var order = CreateLocalOrder();
            var apiClient = new CapturingOrderSyncApiClient(order.OrderGuid);

            await schema.InitializeAsync();
            await orders.SavePendingOrderAsync(order);

            var uploadService = new OrderUploadService(orders, apiClient, uploadRepository);
            await uploadService.UploadOrderAsync(order.OrderGuid);

            var payment = Assert.Single(apiClient.LastRequest!.Payments);
            var transaction = Assert.Single(payment.CardTransactions!);
            Assert.Equal("ANZ", transaction.Processor);
            Assert.Equal("TXN-1", transaction.TxnRef);
            Assert.Equal("merchant receipt", transaction.ReceiptText);
            Assert.Equal(OrderLineKind.Return, apiClient.LastRequest.Lines[1].Kind);
            Assert.Equal("RETURN-UPLOAD-1", apiClient.LastRequest.Lines[1].ReturnSourceKey);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task Order_upload_execution_service_keeps_order_pending_when_cashier_authorization_is_missing()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-order-upload-auth-{Guid.NewGuid():N}.db");

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var orders = new LocalOrderRepository(store);
            var uploadRepository = new LocalOrderUploadRepository(store);
            var syncQueue = new SyncQueueRepository(store);
            var order = CreateLocalOrder();

            await schema.InitializeAsync();
            await orders.SavePendingOrderAsync(order);

            var uploadService = new OrderUploadService(
                orders,
                new UnauthorizedOrderSyncApiClient(),
                uploadRepository);
            var executor = new OrderUploadExecutionService(uploadService, uploadRepository);

            var result = await executor.ExecutePendingAsync();
            var summary = Assert.Single(await orders.GetRecentOrdersAsync());
            var savedOrder = await orders.GetOrderAsync(order.OrderGuid);
            var activeItem = Assert.Single(await syncQueue.GetActiveItemsAsync());

            Assert.Equal(1, result.AttemptedCount);
            Assert.Equal(0, result.UploadedCount);
            Assert.Equal(0, result.FailedCount);
            Assert.Equal("Pending", summary.SyncStatus);
            Assert.Equal("Pending", activeItem.Status);
            Assert.Equal(order.CashierId, savedOrder!.CashierId);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task ExecuteSelectedAsync_counts_missing_authorization_as_failed_and_keeps_order_pending()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-order-upload-selected-auth-{Guid.NewGuid():N}.db");
        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var orders = new LocalOrderRepository(store);
            var uploadRepository = new LocalOrderUploadRepository(store);
            var order = CreateLocalOrder();
            await schema.InitializeAsync();
            await orders.SavePendingOrderAsync(order);
            var executor = new OrderUploadExecutionService(
                new OrderUploadService(orders, new UnauthorizedOrderSyncApiClient(), uploadRepository),
                uploadRepository);

            var result = await executor.ExecuteSelectedAsync([order.OrderGuid]);

            Assert.Equal(new OrderUploadExecutionResult(1, 0, 1), result);
            Assert.Equal("Pending", Assert.Single(await orders.GetRecentOrdersAsync()).SyncStatus);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task ExecuteSelectedAsync_keeps_already_synced_order_synced()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-order-upload-already-synced-{Guid.NewGuid():N}.db");
        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var orders = new LocalOrderRepository(store);
            var uploadRepository = new LocalOrderUploadRepository(store);
            var order = CreateLocalOrder();
            await schema.InitializeAsync();
            await orders.SavePendingOrderAsync(order);
            await uploadRepository.MarkSyncedAsync(order.OrderGuid);
            var executor = new OrderUploadExecutionService(
                new OrderUploadService(
                    orders,
                    new StubOrderSyncApiClient(new OrderSyncResponse(order.OrderGuid, true, true, "AlreadySynced")),
                    uploadRepository),
                uploadRepository);

            var result = await executor.ExecuteSelectedAsync([order.OrderGuid]);

            Assert.Equal(new OrderUploadExecutionResult(1, 1, 0), result);
            Assert.Equal("Synced", Assert.Single(await orders.GetRecentOrdersAsync()).SyncStatus);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task UploadOrderAsync_ignores_voucher_balance_suffix_when_mapping_payment()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-voucher-upload-{Guid.NewGuid():N}.db");

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var orders = new LocalOrderRepository(store);
            var uploadRepository = new LocalOrderUploadRepository(store);
            var order = CreateLocalOrder() with
            {
                Payments =
                [
                    new LocalPayment(
                        Guid.NewGuid(),
                        PaymentMethodKind.Voucher,
                        5.00m,
                        "VOUCHER:ABC123:token-1:12.34")
                ]
            };
            var apiClient = new CapturingOrderSyncApiClient(order.OrderGuid);

            await schema.InitializeAsync();
            await orders.SavePendingOrderAsync(order);

            var uploadService = new OrderUploadService(orders, apiClient, uploadRepository);
            await uploadService.UploadOrderAsync(order.OrderGuid);

            var payment = Assert.Single(apiClient.LastRequest!.Payments);
            Assert.Equal(PaymentMethodKind.Voucher, payment.Method);
            Assert.Equal("ABC123", payment.Reference);
            Assert.Equal("token-1", payment.ReservationToken);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static LocalOrder CreateLocalOrder()
    {
        return new LocalOrder(
            Guid.NewGuid(),
            "S001",
            "POS-01",
            "C001",
            "Alice",
            DateTimeOffset.UtcNow,
            9.20m,
            0.20m,
            9.00m,
            [
                new LocalOrderLine(Guid.NewGuid(), "SKU-101", null, "Organic Gala Apples", "690101", "ITEM-101", 2m, 2.50m, 0m, 5.00m, PriceSourceKind.StoreRetailPrice),
                new LocalOrderLine(
                    Guid.NewGuid(),
                    "SKU-102",
                    null,
                    "Whole Grain Bread",
                    "690102",
                    "ITEM-102",
                    1m,
                    4.20m,
                    0.20m,
                    4.00m,
                    PriceSourceKind.ProductBase,
                    OrderLineKind.Return,
                    "RETURN-UPLOAD-1",
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("22222222-2222-2222-2222-222222222222"))
            ],
            [
                new LocalPayment(
                    Guid.NewGuid(),
                    PaymentMethodKind.Card,
                    9.00m,
                    "ANZ:TXN-1",
                    [
                        new CardTransactionDto(
                            "ANZ",
                            "TXN-1",
                            "123456",
                            "VISA",
                            4,
                            "****1234",
                            "MID-1",
                            "00",
                            "APPROVED",
                            "42",
                            DateTimeOffset.Parse("2026-05-26T00:00:00Z"),
                            9.00m,
                            "merchant receipt")
                    ])
            ]);
    }

    private sealed class StubOrderSyncApiClient(OrderSyncResponse response) : IOrderSyncApiClient
    {
        public Task<OrderSyncResponse> SyncAsync(OrderSyncRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(response);
        }
    }

    private sealed class CapturingOrderSyncApiClient(Guid orderGuid) : IOrderSyncApiClient
    {
        public OrderSyncRequest? LastRequest { get; private set; }

        public Task<OrderSyncResponse> SyncAsync(OrderSyncRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new OrderSyncResponse(orderGuid, true, false, "Synced"));
        }
    }

    private sealed class ThrowingOrderSyncApiClient(string message) : IOrderSyncApiClient
    {
        public Task<OrderSyncResponse> SyncAsync(OrderSyncRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class UnauthorizedOrderSyncApiClient : IOrderSyncApiClient
    {
        public Task<OrderSyncResponse> SyncAsync(
            OrderSyncRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new CatalogApiException("forbidden", HttpStatusCode.Forbidden);
        }
    }

    private sealed class RecordingOrderUploadService(Guid failingOrder) : IOrderUploadService
    {
        public List<Guid> Attempts { get; } = [];

        public Task UploadOrderAsync(Guid orderGuid, CancellationToken cancellationToken = default)
        {
            Attempts.Add(orderGuid);
            return orderGuid == failingOrder
                ? Task.FromException(new InvalidOperationException("failed"))
                : Task.CompletedTask;
        }
    }

    private sealed class CallerCancellationOrderUploadService : IOrderUploadService
    {
        private readonly TaskCompletionSource _firstRequestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstRequestStarted => _firstRequestStarted.Task;

        public List<Guid> Attempts { get; } = [];

        public async Task UploadOrderAsync(Guid orderGuid, CancellationToken cancellationToken = default)
        {
            Attempts.Add(orderGuid);
            _firstRequestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class BlockingOrderUploadService : IOrderUploadService
    {
        private readonly TaskCompletionSource _firstRequestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstRequest =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeRequests;

        public Task FirstRequestStarted => _firstRequestStarted.Task;

        public List<Guid> Attempts { get; } = [];

        public int MaximumConcurrency { get; private set; }

        public void ReleaseFirstRequest() => _releaseFirstRequest.TrySetResult();

        public async Task UploadOrderAsync(Guid orderGuid, CancellationToken cancellationToken = default)
        {
            Attempts.Add(orderGuid);
            var active = Interlocked.Increment(ref _activeRequests);
            MaximumConcurrency = Math.Max(MaximumConcurrency, active);
            try
            {
                if (Attempts.Count == 1)
                {
                    _firstRequestStarted.TrySetResult();
                    await _releaseFirstRequest.Task.WaitAsync(cancellationToken);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeRequests);
            }
        }
    }

    private sealed class EndpointSwitchOrderSyncHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _firstRequestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        public Task FirstRequestStarted => _firstRequestStarted.Task;

        public List<Uri> RequestUris { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            var syncRequest = await request.Content!.ReadFromJsonAsync<OrderSyncRequest>(
                cancellationToken: cancellationToken);
            if (Interlocked.Increment(ref _requestCount) == 1)
            {
                _firstRequestStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(ApiResult<OrderSyncResponse>.Ok(
                    new OrderSyncResponse(syncRequest!.OrderGuid, true, true, "AlreadySynced")))
            };
        }
    }

    private sealed class StubOrderUploadRepository : ILocalOrderUploadRepository
    {
        public Task<IReadOnlyList<Guid>> GetPendingOrderGuidsAsync(int take = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task MarkSyncingAsync(Guid orderGuid, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkPendingAsync(Guid orderGuid, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkSyncedAsync(Guid orderGuid, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkFailedAsync(Guid orderGuid, string errorMessage, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
