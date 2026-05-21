using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Wpf.Models;

public sealed record LocalOrderSummary(
    Guid OrderGuid,
    string StoreCode,
    string DeviceCode,
    string CashierName,
    DateTimeOffset SoldAt,
    decimal TotalAmount,
    decimal DiscountAmount,
    decimal ActualAmount,
    string SyncStatus,
    int LineCount,
    string PaymentSummary)
{
    public string ShortOrderId => OrderGuid.ToString("N")[..8].ToUpperInvariant();

    public string SoldAtDisplay => SoldAt.ToLocalTime().ToString("MMM dd, yyyy HH:mm");

    public string StatusLabel => SyncStatus;
}

public sealed record ReceiptPreviewLine(
    string DisplayName,
    string LookupCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal ActualAmount)
{
    public string QuantityDisplay => Quantity.ToString("0.##");
}

public sealed record ReceiptPaymentLine(
    PaymentMethodKind Method,
    decimal Amount,
    string? Reference)
{
    public string MethodLabel => Method.ToString();
}
