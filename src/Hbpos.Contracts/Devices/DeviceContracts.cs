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
    bool IsAllowed,
    string? Message = null);
