namespace Hbpos.Contracts.Cashiers;

public sealed record CashierBarcodeLoginRequest(
    string StoreCode,
    string UserBarcode,
    string DeviceCode);

public sealed record CashierSessionDto(
    string CashierId,
    string CashierName,
    string StoreCode,
    string DeviceCode,
    string[] Roles);
