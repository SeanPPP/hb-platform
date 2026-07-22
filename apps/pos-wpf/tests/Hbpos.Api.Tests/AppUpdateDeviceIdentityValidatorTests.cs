using Hbpos.Api;
using Hbpos.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Hbpos.Api.Tests;

public sealed class AppUpdateDeviceIdentityValidatorTests
{
    [Fact]
    public async Task ValidateAsync_accepts_latest_enabled_POS_Windows_registration()
    {
        var validator = CreateValidator(new AppUpdateDeviceRegistrationSnapshot(
            "HW-001",
            "auth-secret",
            DeviceStatus: 1,
            DeviceType: "POS",
            DeviceSystem: "Windows"));

        var result = await validator.ValidateAsync(" HW-001 ", " auth-secret ", CancellationToken.None);

        Assert.Equal(new AppUpdateValidatedDeviceIdentity("HW-001"), result);
    }

    [Theory]
    [InlineData(0, "POS", "Windows")]
    [InlineData(1, "Mobile", "Windows")]
    [InlineData(1, "POS", "iOS")]
    public async Task ValidateAsync_rejects_disabled_or_non_POS_Windows_registration(
        int deviceStatus,
        string deviceType,
        string deviceSystem)
    {
        var validator = CreateValidator(new AppUpdateDeviceRegistrationSnapshot(
            "HW-001",
            "auth-secret",
            deviceStatus,
            deviceType,
            deviceSystem));

        var result = await validator.ValidateAsync("HW-001", "auth-secret", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_rejects_lookup_hardware_mismatch()
    {
        var validator = CreateValidator(new AppUpdateDeviceRegistrationSnapshot(
            "HW-FORGED",
            "auth-secret",
            DeviceStatus: 1,
            DeviceType: "POS",
            DeviceSystem: "Windows"));

        var result = await validator.ValidateAsync("HW-001", "auth-secret", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_rejects_old_auth_when_latest_hardware_registration_has_new_auth()
    {
        var validator = CreateValidator(new AppUpdateDeviceRegistrationSnapshot(
            "HW-001",
            "new-auth-secret",
            DeviceStatus: 1,
            DeviceType: "POS",
            DeviceSystem: "Windows"));

        var result = await validator.ValidateAsync("HW-001", "old-auth-secret", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void LatestRegistrationSql_selects_global_latest_hardware_record_before_validation()
    {
        var sql = AppUpdateDeviceIdentityValidator.LatestRegistrationSql;

        Assert.Contains("SELECT TOP 1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[设备硬件识别码] = @HardwareId", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY [ID] DESC", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@AuthorizationCode", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@StoreCode", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@DeviceCode", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void AddHbposApiServices_registers_update_specific_identity_validator()
    {
        var services = new ServiceCollection();

        services.AddHbposApiServices();

        var descriptor = Assert.Single(
            services,
            item => item.ServiceType == typeof(IAppUpdateDeviceIdentityValidator));
        Assert.Equal(typeof(AppUpdateDeviceIdentityValidator), descriptor.ImplementationType);
    }

    private static AppUpdateDeviceIdentityValidator CreateValidator(
        AppUpdateDeviceRegistrationSnapshot? snapshot)
    {
        return new AppUpdateDeviceIdentityValidator(
            (hardwareId, cancellationToken) => Task.FromResult(snapshot));
    }
}
