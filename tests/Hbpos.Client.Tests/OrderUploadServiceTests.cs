using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Tests;

public sealed class OrderUploadServiceTests
{
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
                new LocalOrderLine(Guid.NewGuid(), "SKU-102", null, "Whole Grain Bread", "690102", "ITEM-102", 1m, 4.20m, 0.20m, 4.00m, PriceSourceKind.ProductBase)
            ],
            [new LocalPayment(Guid.NewGuid(), PaymentMethodKind.Card, 9.00m, "CARD-REF-01")]);
    }

    private sealed class StubOrderSyncApiClient(OrderSyncResponse response) : IOrderSyncApiClient
    {
        public Task<OrderSyncResponse> SyncAsync(OrderSyncRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingOrderSyncApiClient(string message) : IOrderSyncApiClient
    {
        public Task<OrderSyncResponse> SyncAsync(OrderSyncRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(message);
        }
    }
}
