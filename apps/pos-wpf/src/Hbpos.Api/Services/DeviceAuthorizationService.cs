using Hbpos.Api.Data;
using Hbpos.Contracts.Devices;
using SqlSugar;

namespace Hbpos.Api.Services;

public interface IDeviceAuthorizationService
{
    Task<DeviceAuthorizationValidationResult> ValidateAsync(
        string authorizationCode,
        string deviceCode,
        string storeCode,
        string? hardwareId,
        CancellationToken cancellationToken);
}

public sealed class DeviceAuthorizationService(HbposSqlSugarContext dbContext) : IDeviceAuthorizationService
{
    private const int EnabledStatus = 1;

    internal const string AuthorizationSql = """
        SELECT TOP 1
            [系统设备编号] AS DeviceCode,
            [分店代码] AS StoreCode,
            [设备硬件识别码] AS HardwareId,
            [设备状态] AS DeviceStatus,
            [设备授权码] AS AuthorizationCode,
            [设备系统] AS DeviceSystem,
            [是否允许交易] AS AllowTransactions
        FROM [POSM_设备注册信息表]
        WHERE [设备授权码] = @AuthorizationCode
          AND [系统设备编号] = @DeviceCode
          AND [分店代码] = @StoreCode
        ORDER BY [ID] DESC;
        """;

    public async Task<DeviceAuthorizationValidationResult> ValidateAsync(
        string authorizationCode,
        string deviceCode,
        string storeCode,
        string? hardwareId,
        CancellationToken cancellationToken)
    {
        var normalizedAuthorizationCode = Normalize(authorizationCode);
        var normalizedDeviceCode = Normalize(deviceCode);
        var normalizedStoreCode = Normalize(storeCode);
        var normalizedHardwareId = Normalize(hardwareId);

        if (string.IsNullOrEmpty(normalizedAuthorizationCode)
            || string.IsNullOrEmpty(normalizedDeviceCode)
            || string.IsNullOrEmpty(normalizedStoreCode))
        {
            return DeviceAuthorizationValidationResult.Failed(
                DeviceAuthorizationFailureCodes.Invalid);
        }

        var device = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<DeviceAuthorizationRow>(
            AuthorizationSql,
            new SugarParameter("@AuthorizationCode", normalizedAuthorizationCode),
            new SugarParameter("@DeviceCode", normalizedDeviceCode),
            new SugarParameter("@StoreCode", normalizedStoreCode));

        if (device is null)
        {
            return DeviceAuthorizationValidationResult.Failed(
                DeviceAuthorizationFailureCodes.Invalid);
        }

        if (device.DeviceStatus != EnabledStatus)
        {
            return DeviceAuthorizationValidationResult.Failed(
                DeviceAuthorizationFailureCodes.DeviceDisabled);
        }

        if (!DeviceSystems.TryNormalize(device.DeviceSystem, out var deviceSystem))
        {
            return DeviceAuthorizationValidationResult.Failed(
                DeviceAuthorizationFailureCodes.Invalid);
        }

        // 关键逻辑：Windows 继续兼容旧客户端可缺失的硬件头；iPadOS 已登记设备必须逐次提交并精确匹配。
        if (!DeviceAuthorizationPlatformPolicy.IsHardwareIdAccepted(
                deviceSystem,
                device.HardwareId,
                normalizedHardwareId))
        {
            return DeviceAuthorizationValidationResult.Failed(
                DeviceAuthorizationFailureCodes.Invalid);
        }

        return DeviceAuthorizationValidationResult.Authorized(
            new DeviceAuthorizationResult(
                device.DeviceCode ?? normalizedDeviceCode,
                device.StoreCode ?? normalizedStoreCode,
                device.HardwareId ?? string.Empty,
                deviceSystem,
                device.AllowTransactions));
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private sealed class DeviceAuthorizationRow
    {
        public string? DeviceCode { get; set; }

        public string? StoreCode { get; set; }

        public string? HardwareId { get; set; }

        public int DeviceStatus { get; set; }

        public string? AuthorizationCode { get; set; }

        public string? DeviceSystem { get; set; }

        public bool AllowTransactions { get; set; } = true;
    }
}

public sealed record DeviceAuthorizationResult(
    string DeviceCode,
    string StoreCode,
    string HardwareId,
    string DeviceSystem = DeviceSystems.Windows,
    bool AllowTransactions = true);

public static class DeviceAuthorizationFailureCodes
{
    public const string Invalid = "DEVICE_AUTH_INVALID";

    public const string DeviceDisabled = "DEVICE_DISABLED";
}

public sealed record DeviceAuthorizationValidationResult(
    DeviceAuthorizationResult? Device,
    string? FailureCode)
{
    public static DeviceAuthorizationValidationResult Authorized(
        DeviceAuthorizationResult device)
    {
        return new DeviceAuthorizationValidationResult(device, null);
    }

    public static DeviceAuthorizationValidationResult Failed(string failureCode)
    {
        return new DeviceAuthorizationValidationResult(null, failureCode);
    }
}
