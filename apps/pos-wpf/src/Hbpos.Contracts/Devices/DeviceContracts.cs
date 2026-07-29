namespace Hbpos.Contracts.Devices;

public sealed record DeviceVerifyRequest(
    string DeviceCode,
    string StoreCode,
    string? HardwareId = null,
    string? TerminalName = null,
    string? DeviceSystem = null);

public sealed record DeviceVerifyResponse(
    string DeviceCode,
    string StoreCode,
    string StoreName,
    int DeviceStatus,
    bool IsAllowed,
    string? Message = null,
    string? AuthorizationCode = null);

public sealed record DeviceRegisterRequest(
    string StoreCode,
    string HardwareId,
    string? TerminalName = null,
    string? DeviceSystem = null);

public static class DeviceSystems
{
    public const string Windows = "Windows";

    public const string IpadOs = "iPadOS";

    public static bool TryNormalize(string? value, out string normalized)
    {
        var candidate = (value ?? string.Empty).Trim();
        if (candidate.Length == 0 || string.Equals(candidate, Windows, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Windows;
            return true;
        }

        if (string.Equals(candidate, IpadOs, StringComparison.OrdinalIgnoreCase))
        {
            normalized = IpadOs;
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    public static bool IsIpadOs(string? value) =>
        string.Equals(value?.Trim(), IpadOs, StringComparison.OrdinalIgnoreCase);
}

public sealed record DeviceRegisterResponse(
    string DeviceCode,
    string StoreCode,
    string StoreName,
    int DeviceStatus,
    bool IsAllowed,
    string? Message = null,
    string? AuthorizationCode = null);

public sealed record DeviceReregisterRequest(
    string TargetStoreCode,
    string HardwareId,
    string? TerminalName = null);

public sealed record DeviceReregisterResponse(
    string DeviceCode,
    string StoreCode,
    string StoreName,
    int DeviceStatus,
    bool IsAllowed,
    string? Message = null,
    string? AuthorizationCode = null);
