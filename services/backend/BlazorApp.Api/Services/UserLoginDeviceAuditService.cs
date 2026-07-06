using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    public sealed class UserLoginDeviceAuditService
    {
        private readonly ISqlSugarClient _db;
        private readonly IDeviceRegistrationService _deviceRegistrationService;

        public UserLoginDeviceAuditService(
            SqlSugarContext context,
            IDeviceRegistrationService deviceRegistrationService
        )
        {
            _db = context.Db;
            _deviceRegistrationService = deviceRegistrationService;
        }

        public async Task<LoginDeviceAuditResult> RecordAsync(User user, LoginDeviceAuditInput input)
        {
            var hardwareId = Normalize(input.HardwareId);
            var systemDeviceNumber = Normalize(input.SystemDeviceNumber);

            if (string.IsNullOrWhiteSpace(hardwareId) && string.IsNullOrWhiteSpace(systemDeviceNumber))
            {
                return new LoginDeviceAuditResult(false, false);
            }

            var now = DateTime.UtcNow;
            var lastRecord = await _db.Queryable<UserLoginDeviceRecord>()
                .Where(item => !item.IsDeleted && item.UserGuid == user.UserGUID)
                .Where(item => item.HardwareId != null && item.HardwareId != "")
                .OrderByDescending(item => item.LoginAtUtc)
                .FirstAsync();

            var isDeviceSwitched =
                !string.IsNullOrWhiteSpace(hardwareId)
                && lastRecord != null
                && !string.Equals(lastRecord.HardwareId, hardwareId, StringComparison.OrdinalIgnoreCase);
            var recentSameDeviceCount = string.IsNullOrWhiteSpace(hardwareId)
                ? 0
                : await _db.Queryable<UserLoginDeviceRecord>()
                    .Where(item => !item.IsDeleted && item.UserGuid == user.UserGUID)
                    .Where(item => item.HardwareId == hardwareId)
                    .Where(item => item.LoginAtUtc >= now.AddDays(-30))
                    .CountAsync();
            var isCommonDevice = recentSameDeviceCount >= 2 || await IsEnabledRegisteredDeviceAsync(hardwareId);

            var record = new UserLoginDeviceRecord
            {
                RecordGuid = Guid.NewGuid().ToString(),
                UserGuid = user.UserGUID,
                Username = user.Username,
                HardwareId = hardwareId,
                SystemDeviceNumber = systemDeviceNumber,
                DeviceSystem = Normalize(input.DeviceSystem),
                StoreCode = Normalize(input.StoreCode),
                LoginSource = string.IsNullOrWhiteSpace(input.LoginSource) ? "AppLogin" : input.LoginSource,
                LoginAtUtc = now,
                LoginIp = Normalize(input.LoginIp),
                UserAgent = Normalize(input.UserAgent),
                LocationLatitude = input.LocationLatitude,
                LocationLongitude = input.LocationLongitude,
                LocationAccuracyMeters = input.LocationAccuracy,
                LocationCapturedAtUtc = NormalizeUtc(input.LocationCapturedAtUtc),
                IsDeviceSwitched = isDeviceSwitched,
                IsCommonDevice = isCommonDevice,
                CreatedAt = now,
                CreatedBy = user.Username,
            };

            // 关键位置：登录审计只追加不更新，便于后续回看设备切换和常用设备判断依据。
            await _db.Insertable(record).ExecuteCommandAsync();
            return new LoginDeviceAuditResult(isDeviceSwitched, isCommonDevice);
        }

        public async Task<LoginDeviceAuditResult> RecordDeviceLoginAsync(DeviceLoginAuditInput input)
        {
            var hardwareId = Normalize(input.HardwareId);
            var systemDeviceNumber = Normalize(input.SystemDeviceNumber);
            if (string.IsNullOrWhiteSpace(hardwareId) && string.IsNullOrWhiteSpace(systemDeviceNumber))
            {
                return new LoginDeviceAuditResult(false, false);
            }

            var now = DateTime.UtcNow;
            var recentSameDeviceCount = string.IsNullOrWhiteSpace(hardwareId)
                ? 0
                : await _db.Queryable<UserLoginDeviceRecord>()
                    .Where(item => !item.IsDeleted && item.HardwareId == hardwareId)
                    .Where(item => item.LoginSource == "DeviceLogin")
                    .Where(item => item.LoginAtUtc >= now.AddDays(-30))
                    .CountAsync();
            var isCommonDevice = recentSameDeviceCount >= 2 || await IsEnabledRegisteredDeviceAsync(hardwareId);

            var record = new UserLoginDeviceRecord
            {
                RecordGuid = Guid.NewGuid().ToString(),
                HardwareId = hardwareId,
                SystemDeviceNumber = systemDeviceNumber,
                DeviceSystem = Normalize(input.DeviceSystem),
                StoreCode = Normalize(input.StoreCode),
                LoginSource = "DeviceLogin",
                LoginAtUtc = now,
                LoginIp = Normalize(input.LoginIp),
                UserAgent = Normalize(input.UserAgent),
                LocationLatitude = input.LocationLatitude,
                LocationLongitude = input.LocationLongitude,
                LocationAccuracyMeters = input.LocationAccuracy,
                LocationCapturedAtUtc = NormalizeUtc(input.LocationCapturedAtUtc),
                IsDeviceSwitched = false,
                IsCommonDevice = isCommonDevice,
                CreatedAt = now,
                CreatedBy = "DeviceLogin",
            };

            // 关键位置：设备登录没有账号上下文，也要按设备维度留痕用于门店审计。
            await _db.Insertable(record).ExecuteCommandAsync();
            return new LoginDeviceAuditResult(false, isCommonDevice);
        }

        private async Task<bool> IsEnabledRegisteredDeviceAsync(string? hardwareId)
        {
            if (string.IsNullOrWhiteSpace(hardwareId))
            {
                return false;
            }

            var device = await _deviceRegistrationService.GetDeviceByHardwareIdAsync(hardwareId);
            return device?.设备状态 == 1;
        }

        private static string? Normalize(string? value)
        {
            var trimmed = value?.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }

        private static DateTime? NormalizeUtc(DateTime? value)
        {
            return value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
        }
    }

    public sealed record LoginDeviceAuditInput(
        string LoginSource,
        string? HardwareId,
        string? SystemDeviceNumber,
        string? DeviceSystem,
        string? StoreCode,
        string? LoginIp,
        string? UserAgent,
        double? LocationLatitude,
        double? LocationLongitude,
        double? LocationAccuracy,
        DateTime? LocationCapturedAtUtc
    );

    public sealed record DeviceLoginAuditInput(
        string? HardwareId,
        string? SystemDeviceNumber,
        string? DeviceSystem,
        string? StoreCode,
        string? LoginIp,
        string? UserAgent,
        double? LocationLatitude,
        double? LocationLongitude,
        double? LocationAccuracy,
        DateTime? LocationCapturedAtUtc
    );

    public sealed record LoginDeviceAuditResult(bool IsDeviceSwitched, bool IsCommonDevice);
}
