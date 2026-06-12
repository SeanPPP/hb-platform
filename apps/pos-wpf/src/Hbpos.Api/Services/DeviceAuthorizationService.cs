using Hbpos.Api.Data;
using SqlSugar;

namespace Hbpos.Api.Services;

public interface IDeviceAuthorizationService
{
    Task<DeviceAuthorizationResult?> ValidateAsync(
        string authorizationCode,
        string deviceCode,
        string storeCode,
        string? hardwareId,
        CancellationToken cancellationToken);
}

public sealed class DeviceAuthorizationService(HbposSqlSugarContext dbContext) : IDeviceAuthorizationService
{
    private const int EnabledStatus = 1;

    public async Task<DeviceAuthorizationResult?> ValidateAsync(
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
            return null;
        }

        const string sql = """
            SELECT TOP 1
                [系统设备编号] AS DeviceCode,
                [分店代码] AS StoreCode,
                [设备硬件识别码] AS HardwareId,
                [设备状态] AS DeviceStatus,
                [设备授权码] AS AuthorizationCode
            FROM [POSM_设备注册信息表]
            WHERE [设备授权码] = @AuthorizationCode
              AND [系统设备编号] = @DeviceCode
              AND [分店代码] = @StoreCode
            ORDER BY [ID] DESC;
            """;

        var device = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<DeviceAuthorizationRow>(
            sql,
            new SugarParameter("@AuthorizationCode", normalizedAuthorizationCode),
            new SugarParameter("@DeviceCode", normalizedDeviceCode),
            new SugarParameter("@StoreCode", normalizedStoreCode));

        if (device is null || device.DeviceStatus != EnabledStatus)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(normalizedHardwareId)
            && !string.Equals(device.HardwareId, normalizedHardwareId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new DeviceAuthorizationResult(
            device.DeviceCode ?? normalizedDeviceCode,
            device.StoreCode ?? normalizedStoreCode,
            device.HardwareId ?? string.Empty);
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
    }
}

public sealed record DeviceAuthorizationResult(
    string DeviceCode,
    string StoreCode,
    string HardwareId);
