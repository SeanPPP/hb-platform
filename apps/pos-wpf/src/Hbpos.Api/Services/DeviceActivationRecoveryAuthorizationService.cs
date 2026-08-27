using Hbpos.Api.Data;
using Hbpos.Contracts.Devices;
using SqlSugar;

namespace Hbpos.Api.Services;

public interface IDeviceActivationRecoveryAuthorizationService
{
    Task<DeviceAuthorizationValidationResult> TryAuthorizePreviousDeviceAsync(
        string authorizationCode,
        string deviceCode,
        string storeCode,
        string? hardwareId,
        CancellationToken cancellationToken);
}

/// <summary>
/// 仅为 rebind 成功但响应丢失的旧设备恢复认证入口；最终仍需原收银员票据和同一码校验。
/// </summary>
public sealed class DeviceActivationRecoveryAuthorizationService(
    HbposSqlSugarContext dbContext) : IDeviceActivationRecoveryAuthorizationService
{
    internal const string RecoveryAuthorizationSql = """
        SELECT TOP 1
            source.[系统设备编号] AS DeviceCode,
            source.[分店代码] AS StoreCode,
            source.[设备硬件识别码] AS HardwareId,
            source.[设备系统] AS DeviceSystem
        FROM [dbo].[POSM_DeviceActivationGrant] AS grantRecord
        INNER JOIN [dbo].[POSM_设备注册信息表] AS source
            ON source.[分店代码] = grantRecord.[PreviousStoreCode]
           AND source.[系统设备编号] = grantRecord.[PreviousDeviceCode]
           AND source.[设备硬件识别码] = grantRecord.[ConsumedHardwareId]
        INNER JOIN [dbo].[POSM_设备注册信息表] AS target
            ON target.[ID] = grantRecord.[ConsumedDeviceRegistrationId]
           AND target.[分店代码] = grantRecord.[StoreCode]
           AND target.[系统设备编号] = grantRecord.[ConsumedDeviceCode]
           AND target.[设备硬件识别码] = grantRecord.[ConsumedHardwareId]
           AND target.[设备系统] = grantRecord.[ConsumedDeviceSystem]
           AND target.[设备状态] = 1
        WHERE grantRecord.[ConsumptionKind] = 'Rebind'
          AND grantRecord.[ConsumedAtUtc] IS NOT NULL
          AND grantRecord.[ConsumedHardwareId] = @HardwareId
          AND source.[设备授权码] = @AuthorizationCode
          AND source.[系统设备编号] = @DeviceCode
          AND source.[分店代码] = @StoreCode
          AND source.[设备硬件识别码] = @HardwareId
          AND source.[设备状态] = 0
        ORDER BY grantRecord.[ConsumedAtUtc] DESC;
        """;

    public async Task<DeviceAuthorizationValidationResult> TryAuthorizePreviousDeviceAsync(
        string authorizationCode,
        string deviceCode,
        string storeCode,
        string? hardwareId,
        CancellationToken cancellationToken)
    {
        var normalizedHardwareId = hardwareId?.Trim();
        if (string.IsNullOrWhiteSpace(authorizationCode)
            || string.IsNullOrWhiteSpace(deviceCode)
            || string.IsNullOrWhiteSpace(storeCode)
            || string.IsNullOrWhiteSpace(normalizedHardwareId))
        {
            return DeviceAuthorizationValidationResult.Failed(
                DeviceAuthorizationFailureCodes.Invalid);
        }

        var row = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<RecoveryAuthorizationRow>(
            RecoveryAuthorizationSql,
            new SugarParameter("@AuthorizationCode", authorizationCode.Trim()),
            new SugarParameter("@DeviceCode", deviceCode.Trim()),
            new SugarParameter("@StoreCode", storeCode.Trim()),
            new SugarParameter("@HardwareId", normalizedHardwareId));
        if (row == null
            || !DeviceSystems.TryNormalize(row.DeviceSystem, out var deviceSystem))
        {
            return DeviceAuthorizationValidationResult.Failed(
                DeviceAuthorizationFailureCodes.Invalid);
        }

        return DeviceAuthorizationValidationResult.Authorized(
            new DeviceAuthorizationResult(
                row.DeviceCode ?? deviceCode.Trim(),
                row.StoreCode ?? storeCode.Trim(),
                row.HardwareId ?? normalizedHardwareId,
                deviceSystem,
                AllowTransactions: false));
    }

    private sealed class RecoveryAuthorizationRow
    {
        public string? DeviceCode { get; set; }
        public string? StoreCode { get; set; }
        public string? HardwareId { get; set; }
        public string? DeviceSystem { get; set; }
    }
}
