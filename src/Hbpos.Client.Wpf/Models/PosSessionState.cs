namespace Hbpos.Client.Wpf.Models;

public sealed record PosSessionState(
    string SystemName,
    string StoreCode,
    string StoreName,
    string DeviceCode,
    string CashierId,
    string CashierName,
    bool IsOnline,
    int PendingSyncCount);
