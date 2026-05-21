using Hbpos.Contracts.Catalog;

namespace Hbpos.Contracts.Orders;

public enum PaymentMethodKind
{
    Cash = 1,
    Card = 2,
    QrCode = 3
}

public sealed record OrderSyncRequest(
    Guid OrderGuid,
    string StoreCode,
    string DeviceCode,
    string CashierId,
    string CashierName,
    DateTimeOffset SoldAt,
    decimal TotalAmount,
    decimal DiscountAmount,
    decimal ActualAmount,
    IReadOnlyList<OrderLineSyncDto> Lines,
    IReadOnlyList<PaymentSyncDto> Payments);

public sealed record OrderLineSyncDto(
    Guid OrderLineGuid,
    string ProductCode,
    string? ReferenceCode,
    string DisplayName,
    string LookupCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal ActualAmount,
    PriceSourceKind PriceSource);

public sealed record PaymentSyncDto(
    Guid PaymentGuid,
    PaymentMethodKind Method,
    decimal Amount,
    string? Reference);

public sealed record OrderSyncResponse(
    Guid OrderGuid,
    bool Accepted,
    bool AlreadySynced,
    string? Message = null);
