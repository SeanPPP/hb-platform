namespace Hbpos.Contracts.Devices;

public sealed record DeviceVerifyRequest(
    string DeviceCode,
    string StoreCode,
    string? HardwareId = null,
    string? TerminalName = null);

public sealed record DeviceVerifyResponse(
    string DeviceCode,
    string StoreCode,
    string StoreName,
    int DeviceStatus,
    bool IsAllowed,
    string? Message = null);

public sealed record DeviceRegisterRequest(
    string StoreCode,
    string HardwareId,
    string? TerminalName = null);

public sealed record DeviceRegisterResponse(
    string DeviceCode,
    string StoreCode,
    string StoreName,
    int DeviceStatus,
    bool IsAllowed,
    string? Message = null);
