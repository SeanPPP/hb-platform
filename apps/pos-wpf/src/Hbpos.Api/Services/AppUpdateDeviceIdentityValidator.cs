using Hbpos.Api.Data;
using SqlSugar;

namespace Hbpos.Api.Services;

public interface IAppUpdateDeviceIdentityValidator
{
    Task<AppUpdateValidatedDeviceIdentity?> ValidateAsync(
        string hardwareId,
        string authorizationCode,
        CancellationToken cancellationToken);
}

public sealed record AppUpdateValidatedDeviceIdentity(string HardwareId);

internal sealed record AppUpdateDeviceRegistrationSnapshot(
    string? HardwareId,
    string? AuthorizationCode,
    int DeviceStatus,
    string? DeviceType,
    string? DeviceSystem);

public sealed class AppUpdateDeviceIdentityValidator : IAppUpdateDeviceIdentityValidator
{
    private const int EnabledStatus = 1;
    private const string PosDeviceType = "POS";
    private const string WindowsDeviceSystem = "Windows";

    internal const string LatestRegistrationSql = """
        SELECT TOP 1
            [设备硬件识别码] AS HardwareId,
            [设备授权码] AS AuthorizationCode,
            [设备状态] AS DeviceStatus,
            [设备类型] AS DeviceType,
            [设备系统] AS DeviceSystem
        FROM [POSM_设备注册信息表]
        WHERE [设备硬件识别码] = @HardwareId
        ORDER BY [ID] DESC;
        """;

    private readonly Func<string, CancellationToken, Task<AppUpdateDeviceRegistrationSnapshot?>>
        loadLatestRegistrationAsync;

    public AppUpdateDeviceIdentityValidator(HbposSqlSugarContext dbContext)
        : this((hardwareId, cancellationToken) =>
            LoadLatestRegistrationAsync(dbContext, hardwareId, cancellationToken))
    {
    }

    internal AppUpdateDeviceIdentityValidator(
        Func<string, CancellationToken, Task<AppUpdateDeviceRegistrationSnapshot?>>
            loadLatestRegistrationAsync)
    {
        this.loadLatestRegistrationAsync = loadLatestRegistrationAsync;
    }

    public async Task<AppUpdateValidatedDeviceIdentity?> ValidateAsync(
        string hardwareId,
        string authorizationCode,
        CancellationToken cancellationToken)
    {
        var normalizedHardwareId = Normalize(hardwareId);
        var normalizedAuthorizationCode = Normalize(authorizationCode);
        if (string.IsNullOrEmpty(normalizedHardwareId) || string.IsNullOrEmpty(normalizedAuthorizationCode))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var registration = await loadLatestRegistrationAsync(
            normalizedHardwareId,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // 关键逻辑：先按原始硬件号取全局最新记录，再检查授权码、状态和平台，禁止旧授权或旧启用行回退命中。
        if (registration is null ||
            !string.Equals(registration.AuthorizationCode?.Trim(), normalizedAuthorizationCode, StringComparison.Ordinal) ||
            registration.DeviceStatus != EnabledStatus ||
            !string.Equals(registration.DeviceType, PosDeviceType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(registration.DeviceSystem, WindowsDeviceSystem, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(registration.HardwareId?.Trim(), normalizedHardwareId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new AppUpdateValidatedDeviceIdentity(registration.HardwareId!.Trim());
    }

    private static async Task<AppUpdateDeviceRegistrationSnapshot?> LoadLatestRegistrationAsync(
        HbposSqlSugarContext dbContext,
        string hardwareId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registration = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<AppUpdateDeviceRegistrationRow>(
            LatestRegistrationSql,
            new SugarParameter("@HardwareId", hardwareId));
        cancellationToken.ThrowIfCancellationRequested();
        return registration is null
            ? null
            : new AppUpdateDeviceRegistrationSnapshot(
                registration.HardwareId,
                registration.AuthorizationCode,
                registration.DeviceStatus,
                registration.DeviceType,
                registration.DeviceSystem);
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();

    private sealed class AppUpdateDeviceRegistrationRow
    {
        public string? HardwareId { get; set; }

        public string? AuthorizationCode { get; set; }

        public int DeviceStatus { get; set; }

        public string? DeviceType { get; set; }

        public string? DeviceSystem { get; set; }
    }
}
