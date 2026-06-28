namespace Hbpos.Contracts.Cashiers;

public sealed record CashierBarcodeLoginRequest(
    string StoreCode,
    string UserBarcode,
    string DeviceCode);

public sealed record CashierSessionDto(
    string CashierId,
    string UserGuid,
    string CashierName,
    string StoreCode,
    string DeviceCode,
    string[] Roles,
    string[] PermissionCodes,
    string[] AllowedStoreCodes,
    bool IsSuperAdmin,
    bool IsOfflineCached,
    bool IsEmergencyOverride);
