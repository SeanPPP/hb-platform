using Hbpos.Client.Wpf.Models;

namespace Hbpos.Client.Wpf.Services;

public interface ISuspendedOrderService
{
    Task<SuspendedOrder> SuspendCurrentOrderAsync(
        PosSessionState session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SuspendedOrderSummary>> GetPendingOrdersAsync(
        string storeCode,
        string? deviceCode = null,
        string? keyword = null,
        int take = 100,
        CancellationToken cancellationToken = default);

    Task<SuspendedOrder?> GetOrderAsync(
        Guid suspendedOrderGuid,
        CancellationToken cancellationToken = default);

    Task<SuspendedOrder> RecallOrderAsync(
        Guid suspendedOrderGuid,
        CancellationToken cancellationToken = default);
}

public sealed class SuspendedOrderService(
    ISuspendedOrderRepository repository,
    PosCartService cart) : ISuspendedOrderService
{
    public async Task<SuspendedOrder> SuspendCurrentOrderAsync(
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        if (cart.IsEmpty)
        {
            throw new InvalidOperationException("Cart is empty.");
        }

        var sessionSnapshot = session with { };
        var orderGuid = Guid.NewGuid();
        var snapshot = cart.CreateSnapshot();
        var lines = snapshot.Lines
            .Select(line => new SuspendedOrderLine(
                Guid.NewGuid(),
                orderGuid,
                line.StoreCode,
                line.ProductCode,
                line.ReferenceCode,
                line.DisplayName,
                line.LookupCode,
                line.ItemNumber,
                line.ProductImage,
                line.Quantity,
                line.UnitPrice,
                line.DiscountAmount,
                line.DiscountPercent,
                CalculateActualAmount(line),
                line.PriceSource,
                line.PriceSourceLabel,
                line.DiscountSource)
            {
                Kind = line.Kind,
                ReturnSourceKey = line.ReturnSourceKey,
                OriginalOrderGuid = line.OriginalOrderGuid,
                OriginalOrderDetailGuid = line.OriginalOrderLineGuid,
                ReturnReason = line.ReturnReason
            })
            .ToArray();

        var order = new SuspendedOrder(
            orderGuid,
            sessionSnapshot.StoreCode,
            sessionSnapshot.DeviceCode,
            sessionSnapshot.CashierId,
            sessionSnapshot.CashierName,
            DateTimeOffset.Now,
            cart.TotalAmount,
            cart.DiscountAmount,
            cart.ActualAmount,
            SuspendedOrderStatus.Pending,
            lines)
        {
            ReturnPaymentCapacities = cart.ReturnPaymentCapacities.ToArray()
        };

        await Task.Run(() => repository.SaveAsync(order, cancellationToken), cancellationToken);
        cart.Clear();
        return order;
    }

    public Task<IReadOnlyList<SuspendedOrderSummary>> GetPendingOrdersAsync(
        string storeCode,
        string? deviceCode = null,
        string? keyword = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => repository.GetPendingAsync(storeCode, deviceCode, keyword, take, cancellationToken),
            cancellationToken);
    }

    public Task<SuspendedOrder?> GetOrderAsync(
        Guid suspendedOrderGuid,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => repository.GetAsync(suspendedOrderGuid, cancellationToken),
            cancellationToken);
    }

    public async Task<SuspendedOrder> RecallOrderAsync(
        Guid suspendedOrderGuid,
        CancellationToken cancellationToken = default)
    {
        if (!cart.IsEmpty)
        {
            throw new InvalidOperationException("Cart must be empty before recalling a suspended order.");
        }

        var order = await Task.Run(
                () => repository.GetAsync(suspendedOrderGuid, cancellationToken),
                cancellationToken)
            ?? throw new InvalidOperationException("Suspended order was not found.");
        if (order.Status != SuspendedOrderStatus.Pending)
        {
            throw new InvalidOperationException("Suspended order is not pending.");
        }

        cart.RestoreSnapshot(new PosCartSnapshot(order.Lines
            .Select(line => new PosCartLineSnapshot(
                line.StoreCode,
                line.ProductCode,
                line.ReferenceCode,
                line.DisplayName,
                line.LookupCode,
                line.ItemNumber,
                line.ProductImage,
                line.Quantity,
                line.UnitPrice,
                line.DiscountAmount,
                line.DiscountPercent,
                line.PriceSource,
                line.PriceSourceLabel,
                line.Kind,
                line.ReturnSourceKey,
                line.OriginalOrderGuid,
                line.OriginalOrderDetailGuid,
                line.ReturnReason,
                line.DiscountSource))
            .ToArray()));
        cart.AddReturnPaymentCapacities(order.ReturnPaymentCapacities);
        try
        {
            // 购物车已恢复后进入不可取消提交段，避免 caller token 让 DB 仍为 Pending。
            await Task.Run(
                () => repository.MarkStatusAsync(
                    suspendedOrderGuid,
                    SuspendedOrderStatus.Recalled,
                    CancellationToken.None),
                CancellationToken.None);
        }
        catch
        {
            // 召回前已要求空购物车；状态提交失败时清回空状态，允许安全重试。
            cart.Clear();
            throw;
        }

        return order with { Status = SuspendedOrderStatus.Recalled };
    }

    private static decimal CalculateActualAmount(PosCartLineSnapshot line)
    {
        var actualAmount = decimal.Round(line.Quantity * line.UnitPrice - line.DiscountAmount, 2, MidpointRounding.AwayFromZero);
        return line.Kind == CartLineKind.Return ? -actualAmount : actualAmount;
    }
}
