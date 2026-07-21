using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Diagnostics;
using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Orders;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Wpf.Services;

public interface IOrderUploadService
{
    Task UploadOrderAsync(Guid orderGuid, CancellationToken cancellationToken = default);
}

public sealed class OrderUploadAuthorizationRequiredException(string message, Exception innerException)
    : Exception(message, innerException);

public interface IOrderUploadExecutionService
{
    Task<OrderUploadExecutionResult> ExecuteOneAsync(Guid orderGuid, CancellationToken cancellationToken = default);

    Task<OrderUploadExecutionResult> ExecutePendingAsync(int batchSize = 20, CancellationToken cancellationToken = default);

    async Task<OrderUploadExecutionResult> ExecuteSelectedAsync(
        IReadOnlyCollection<Guid> orderGuids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderGuids);
        var selected = orderGuids.Where(id => id != Guid.Empty).Distinct().ToArray();
        var uploaded = 0;
        var failed = 0;
        foreach (var orderGuid in selected)
        {
            var result = await ExecuteOneAsync(orderGuid, cancellationToken);
            uploaded += result.UploadedCount;
            failed += result.FailedCount;
        }

        return new OrderUploadExecutionResult(selected.Length, uploaded, failed);
    }
}

public sealed class OrderUploadService(
    ILocalOrderRepository orderRepository,
    IOrderSyncApiClient apiClient,
    ILocalOrderUploadRepository uploadRepository) : IOrderUploadService
{
    public async Task UploadOrderAsync(Guid orderGuid, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        Log($"upload start orderGuid={orderGuid:D}");
        var order = await orderRepository.GetOrderAsync(orderGuid, cancellationToken)
            ?? throw new InvalidOperationException("Order was not found for upload.");
        try
        {
            await uploadRepository.MarkSyncingAsync(orderGuid, cancellationToken);
            Log(
                $"mark syncing orderGuid={orderGuid:D} store={order.StoreCode} device={order.DeviceCode} " +
                $"lines={order.Lines.Count} payments={order.Payments.Count} actualAmount={order.ActualAmount}");
            var request = ToRequest(order);
            var response = await apiClient.SyncAsync(request, cancellationToken);
            if (!response.Accepted)
            {
                throw new InvalidOperationException(response.Message ?? "Order sync was not accepted.");
            }

            await uploadRepository.MarkSyncedAsync(orderGuid, cancellationToken);
            Log(
                $"upload completed orderGuid={orderGuid:D} accepted={response.Accepted} alreadySynced={response.AlreadySynced} " +
                $"message={response.Message ?? "<null>"} elapsedMs={stopwatch.ElapsedMilliseconds}");
        }
        catch (CatalogApiException ex) when (
            ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // 收银员票据缺失或过期不是订单失败；恢复 Pending，等待下一次有效登录。
            await uploadRepository.MarkPendingAsync(orderGuid, cancellationToken);
            Log($"upload deferred orderGuid={orderGuid:D} reason=cashier-authorization-required");
            throw new OrderUploadAuthorizationRequiredException("需要有效收银员授权后再上传订单。", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await uploadRepository.MarkFailedAsync(orderGuid, ex.Message, cancellationToken);
            Log(
                $"upload failed orderGuid={orderGuid:D} error={ex.GetType().Name} message={ex.Message} " +
                $"elapsedMs={stopwatch.ElapsedMilliseconds}");
            throw;
        }
    }

    private static OrderSyncRequest ToRequest(LocalOrder order)
    {
        return new OrderSyncRequest(
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
                line.OriginalOrderDetailGuid)).ToList(),
            order.Payments.Select(ToPaymentSyncDto).ToList());
    }

    private static PaymentSyncDto ToPaymentSyncDto(LocalPayment payment)
    {
        if (payment.Method == PaymentMethodKind.Voucher)
        {
            var (voucherCode, reservationToken) = ParseVoucherReference(payment.Reference);
            return new PaymentSyncDto(
                payment.PaymentGuid,
                payment.Method,
                payment.Amount,
                voucherCode,
                reservationToken,
                payment.CardTransactions);
        }

        return new PaymentSyncDto(
            payment.PaymentGuid,
            payment.Method,
            payment.Amount,
            payment.Reference,
            CardTransactions: payment.CardTransactions);
    }

    internal static (string VoucherCode, string ReservationToken) ParseVoucherReference(string? reference)
    {
        var parts = (reference ?? string.Empty).Split(':', StringSplitOptions.TrimEntries);
        return parts.Length >= 3 && parts[0].Equals("VOUCHER", StringComparison.OrdinalIgnoreCase)
            ? (parts[1], parts[2])
            : parts.Length >= 2 && parts[0].Equals("VOUCHER_REFUND", StringComparison.OrdinalIgnoreCase)
                ? (parts[1], string.Empty)
            : (reference ?? string.Empty, string.Empty);
    }

    private static void Log(string message)
    {
        ConsoleLog.Write("OrderSync", message);
    }
}

public sealed class OrderUploadExecutionService(
    IOrderUploadService uploadService,
    ILocalOrderUploadRepository uploadRepository) : IOrderUploadExecutionService
{
    // ponytail: 单客户端全局串行足以消除状态覆盖；仅在实测吞吐不足时升级为按订单 GUID 加锁。
    private readonly SemaphoreSlim _executionGate = new(1, 1);

    public async Task<OrderUploadExecutionResult> ExecuteSelectedAsync(
        IReadOnlyCollection<Guid> orderGuids,
        CancellationToken cancellationToken = default)
    {
        await _executionGate.WaitAsync(cancellationToken);
        try
        {
            ArgumentNullException.ThrowIfNull(orderGuids);
            // 保留收银员勾选顺序并去重，串行上传可避免同一订单的状态互相覆盖。
            var selected = orderGuids.Where(id => id != Guid.Empty).Distinct().ToArray();
            var uploadedCount = 0;
            var failedCount = 0;
            for (var index = 0; index < selected.Length; index++)
            {
                var orderGuid = selected[index];
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await uploadService.UploadOrderAsync(orderGuid, cancellationToken);
                    uploadedCount++;
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // 端点发布前新请求仍被封闭；将当前项和剩余选择统一入队，交给新端点继续上传。
                    for (var pendingIndex = index; pendingIndex < selected.Length; pendingIndex++)
                    {
                        await uploadRepository.MarkPendingAsync(selected[pendingIndex], cancellationToken);
                    }

                    failedCount += selected.Length - index;
                    Log(
                        $"execute selected batch interrupted orderGuid={orderGuid:D} queued={selected.Length - index} " +
                        "reason=endpoint-generation-canceled " +
                        $"error={ex.GetType().Name}");
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (OrderUploadAuthorizationRequiredException)
                {
                    failedCount++;
                    Log($"execute selected item deferred orderGuid={orderGuid:D} reason=cashier-authorization-required");
                }
                catch (Exception ex)
                {
                    failedCount++;
                    Log($"execute selected item failed orderGuid={orderGuid:D} error={ex.GetType().Name} message={ex.Message}");
                }
            }

            return new OrderUploadExecutionResult(selected.Length, uploadedCount, failedCount);
        }
        finally
        {
            _executionGate.Release();
        }
    }

    public async Task<OrderUploadExecutionResult> ExecuteOneAsync(Guid orderGuid, CancellationToken cancellationToken = default)
    {
        await _executionGate.WaitAsync(cancellationToken);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            Log($"execute one start orderGuid={orderGuid:D}");
            try
            {
                await uploadService.UploadOrderAsync(orderGuid, cancellationToken);
                Log($"execute one completed orderGuid={orderGuid:D} uploaded=1 failed=0 elapsedMs={stopwatch.ElapsedMilliseconds}");
                return new OrderUploadExecutionResult(1, 1, 0);
            }
            catch (OperationCanceledException)
            {
                Log($"execute one canceled orderGuid={orderGuid:D} elapsedMs={stopwatch.ElapsedMilliseconds}");
                throw;
            }
            catch (OrderUploadAuthorizationRequiredException)
            {
                Log($"execute one deferred orderGuid={orderGuid:D} reason=cashier-authorization-required");
                return new OrderUploadExecutionResult(1, 0, 0);
            }
            catch (Exception ex)
            {
                Log($"execute one failed orderGuid={orderGuid:D} error={ex.GetType().Name} message={ex.Message} elapsedMs={stopwatch.ElapsedMilliseconds}");
                return new OrderUploadExecutionResult(1, 0, 1);
            }
        }
        finally
        {
            _executionGate.Release();
        }
    }

    public async Task<OrderUploadExecutionResult> ExecutePendingAsync(int batchSize = 20, CancellationToken cancellationToken = default)
    {
        await _executionGate.WaitAsync(cancellationToken);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            Log($"execute pending start batchSize={batchSize}");
            var orderGuids = await uploadRepository.GetPendingOrderGuidsAsync(batchSize, cancellationToken);
            Log($"execute pending queued count={orderGuids.Count} batchSize={batchSize}");
            var uploadedCount = 0;
            var failedCount = 0;

            foreach (var orderGuid in orderGuids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await uploadService.UploadOrderAsync(orderGuid, cancellationToken);
                    uploadedCount++;
                    Log($"execute pending item completed orderGuid={orderGuid:D} uploadedCount={uploadedCount} failedCount={failedCount}");
                }
                catch (OperationCanceledException)
                {
                    Log(
                        $"execute pending canceled orderGuid={orderGuid:D} attempted={orderGuids.Count} uploaded={uploadedCount} " +
                        $"failed={failedCount} elapsedMs={stopwatch.ElapsedMilliseconds}");
                    throw;
                }
                catch (OrderUploadAuthorizationRequiredException)
                {
                    Log($"execute pending item deferred orderGuid={orderGuid:D} reason=cashier-authorization-required");
                }
                catch (Exception ex)
                {
                    failedCount++;
                    Log(
                        $"execute pending item failed orderGuid={orderGuid:D} uploadedCount={uploadedCount} failedCount={failedCount} " +
                        $"error={ex.GetType().Name} message={ex.Message}");
                }
            }

            Log(
                $"execute pending completed attempted={orderGuids.Count} uploaded={uploadedCount} failed={failedCount} " +
                $"elapsedMs={stopwatch.ElapsedMilliseconds}");
            return new OrderUploadExecutionResult(orderGuids.Count, uploadedCount, failedCount);
        }
        finally
        {
            _executionGate.Release();
        }
    }

    private static void Log(string message)
    {
        ConsoleLog.Write("OrderSync", message);
    }
}

public sealed class NoopOrderUploadExecutionService : IOrderUploadExecutionService
{
    public static NoopOrderUploadExecutionService Instance { get; } = new();

    private NoopOrderUploadExecutionService()
    {
    }

    public Task<OrderUploadExecutionResult> ExecuteOneAsync(Guid orderGuid, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OrderUploadExecutionResult(0, 0, 0));
    }

    public Task<OrderUploadExecutionResult> ExecutePendingAsync(int batchSize = 20, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OrderUploadExecutionResult(0, 0, 0));
    }

    public Task<OrderUploadExecutionResult> ExecuteSelectedAsync(
        IReadOnlyCollection<Guid> orderGuids,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OrderUploadExecutionResult(orderGuids?.Distinct().Count() ?? 0, 0, 0));
    }
}

public interface IOrderSyncApiClient
{
    Task<OrderSyncResponse> SyncAsync(OrderSyncRequest request, CancellationToken cancellationToken = default);
}

public sealed class OrderSyncApiClient(HttpClient httpClient) : IOrderSyncApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<OrderSyncResponse> SyncAsync(OrderSyncRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        const string requestPath = "/api/v1/orders/sync";
        Log(
            $"http sync start orderGuid={request.OrderGuid:D} store={request.StoreCode} device={request.DeviceCode} " +
            $"lines={request.Lines.Count} payments={request.Payments.Count}");
        using var response = await httpClient.PostAsJsonAsync(requestPath.TrimStart('/'), request, JsonOptions, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        ApiResult<OrderSyncResponse>? result = null;
        if (!string.IsNullOrWhiteSpace(content))
        {
            result = JsonSerializer.Deserialize<ApiResult<OrderSyncResponse>>(content, JsonOptions);
        }

        if (!response.IsSuccessStatusCode || result is null || !result.Success || result.Data is null)
        {
            Log(
                $"http sync failed orderGuid={request.OrderGuid:D} http={(int)response.StatusCode} " +
                $"success={result?.Success.ToString() ?? "<null>"} errorCode={result?.ErrorCode ?? "<null>"} " +
                $"message={result?.Message ?? "<null>"} elapsedMs={stopwatch.ElapsedMilliseconds}");
            ConsoleLog.WriteError(
                "OrderSync",
                "Order sync request failed.",
                new ApplicationLogContext(
                    TraceId: request.OrderGuid.ToString("D"),
                    RequestPath: requestPath,
                    RequestMethod: "POST",
                    StatusCode: (int)response.StatusCode,
                    Properties: new Dictionary<string, object?>
                    {
                        ["storeCode"] = request.StoreCode,
                        ["deviceCode"] = request.DeviceCode,
                        ["errorCode"] = result?.ErrorCode,
                        ["elapsedMs"] = stopwatch.ElapsedMilliseconds
                    }));
            throw new CatalogApiException(
                result?.Message ?? $"Order sync failed with HTTP {(int)response.StatusCode}.",
                response.StatusCode,
                result?.ErrorCode);
        }

        Log(
            $"http sync completed orderGuid={request.OrderGuid:D} http={(int)response.StatusCode} accepted={result.Data.Accepted} " +
            $"alreadySynced={result.Data.AlreadySynced} message={result.Data.Message ?? "<null>"} elapsedMs={stopwatch.ElapsedMilliseconds}");
        return result.Data;
    }

    private static void Log(string message)
    {
        ConsoleLog.Write("OrderSync", message);
    }
}

public interface ILocalOrderUploadRepository
{
    Task<IReadOnlyList<Guid>> GetPendingOrderGuidsAsync(int take = 20, CancellationToken cancellationToken = default);

    Task MarkSyncingAsync(Guid orderGuid, CancellationToken cancellationToken = default);

    Task MarkPendingAsync(Guid orderGuid, CancellationToken cancellationToken = default);

    Task MarkSyncedAsync(Guid orderGuid, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(Guid orderGuid, string errorMessage, CancellationToken cancellationToken = default);
}

public sealed class LocalOrderUploadRepository(LocalSqliteStore store) : ILocalOrderUploadRepository
{
    public async Task<IReadOnlyList<Guid>> GetPendingOrderGuidsAsync(int take = 20, CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT EntityId
            FROM SyncQueue
            WHERE EntityType = 'Order'
              AND Status IN ('Pending', 'Failed')
            ORDER BY CreatedAt
            LIMIT $Take;
            """;
        command.Parameters.AddWithValue("$Take", Math.Clamp(take, 1, 100));

        var orderGuids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (Guid.TryParse(reader.GetString(0), out var orderGuid))
            {
                orderGuids.Add(orderGuid);
            }
        }

        return orderGuids;
    }

    public Task MarkSyncingAsync(Guid orderGuid, CancellationToken cancellationToken = default)
    {
        return UpdateStatusAsync(orderGuid, "Syncing", null, cancellationToken);
    }

    public Task MarkPendingAsync(Guid orderGuid, CancellationToken cancellationToken = default)
    {
        return UpdateStatusAsync(orderGuid, "Pending", null, cancellationToken);
    }

    public Task MarkSyncedAsync(Guid orderGuid, CancellationToken cancellationToken = default)
    {
        return UpdateStatusAsync(orderGuid, "Synced", null, cancellationToken);
    }

    public Task MarkFailedAsync(Guid orderGuid, string errorMessage, CancellationToken cancellationToken = default)
    {
        return UpdateStatusAsync(orderGuid, "Failed", errorMessage, cancellationToken);
    }

    private async Task UpdateStatusAsync(
        Guid orderGuid,
        string status,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await using (var orderCommand = connection.CreateCommand())
        {
            orderCommand.Transaction = transaction;
            orderCommand.CommandText = "UPDATE LocalOrders SET SyncStatus = $Status WHERE OrderGuid = $OrderGuid;";
            orderCommand.Parameters.AddWithValue("$Status", status);
            orderCommand.Parameters.AddWithValue("$OrderGuid", orderGuid.ToString());
            await orderCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var queueCommand = connection.CreateCommand())
        {
            queueCommand.Transaction = transaction;
            queueCommand.CommandText = """
                UPDATE SyncQueue
                SET Status = $Status,
                    LastTriedAt = $LastTriedAt,
                    ErrorMessage = $ErrorMessage
                WHERE EntityId = $OrderGuid AND EntityType = 'Order';
                """;
            queueCommand.Parameters.AddWithValue("$Status", status == "Synced" ? "Synced" : status);
            queueCommand.Parameters.AddWithValue("$LastTriedAt", DateTimeOffset.Now.ToString("O"));
            queueCommand.Parameters.AddWithValue("$ErrorMessage", (object?)errorMessage ?? DBNull.Value);
            queueCommand.Parameters.AddWithValue("$OrderGuid", orderGuid.ToString());
            await queueCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed record OrderUploadExecutionResult(
    int AttemptedCount,
    int UploadedCount,
    int FailedCount);
