namespace Hbpos.Client.Wpf.Models;

public sealed record LocalDeviceCache(
    string DeviceCode,
    string StoreCode,
    string StoreName,
    string HardwareId,
    int DeviceStatus,
    bool IsAllowed,
    string? Message,
    DateTimeOffset UpdatedAt,
    string? AuthorizationCode = null);

public sealed record StoreSelectionItem(
    string StoreCode,
    string StoreName,
    bool IsActive)
{
    public string DisplayName => $"{StoreName} ({StoreCode})";
}
