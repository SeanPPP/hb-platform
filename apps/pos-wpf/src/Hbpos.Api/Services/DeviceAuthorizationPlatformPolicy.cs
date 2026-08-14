using Hbpos.Contracts.Devices;

namespace Hbpos.Api.Services;

/// <summary>
/// 设备认证的平台注册规则，避免 Windows 兼容判断在各认证入口分叉。
/// </summary>
public static class DeviceAuthorizationPlatformPolicy
{
    public static bool IsHardwareIdAccepted(
        string? deviceSystem,
        string? registeredHardwareId,
        string? submittedHardwareId)
    {
        // 关键逻辑：仅空值/Windows 属于旧客户端兼容范围；未知非空平台必须关闭认证。
        if (!DeviceSystems.TryNormalize(deviceSystem, out var normalizedDeviceSystem))
        {
            return false;
        }

        var normalizedSubmittedHardwareId = (submittedHardwareId ?? string.Empty).Trim();
        if (DeviceSystems.RequiresExactHardwareId(normalizedDeviceSystem)
            && normalizedSubmittedHardwareId.Length == 0)
        {
            return false;
        }

        return normalizedSubmittedHardwareId.Length == 0
            || string.Equals(
                (registeredHardwareId ?? string.Empty).Trim(),
                normalizedSubmittedHardwareId,
                StringComparison.OrdinalIgnoreCase);
    }
}
