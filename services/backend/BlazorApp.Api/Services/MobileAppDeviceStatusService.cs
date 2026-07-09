using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    public sealed class MobileAppDeviceStatusService
    {
        public const int OnlineWindowMinutes = 15;

        private readonly ISqlSugarClient _db;
        private readonly IDeviceRegistrationService _deviceRegistrationService;
        private readonly ILogger<MobileAppDeviceStatusService> _logger;
        private readonly Func<DateTime> _utcNow;

        public MobileAppDeviceStatusService(
            ISqlSugarClient db,
            IDeviceRegistrationService deviceRegistrationService,
            ILogger<MobileAppDeviceStatusService> logger,
            Func<DateTime>? utcNowProvider = null
        )
        {
            _db = db;
            _deviceRegistrationService = deviceRegistrationService;
            _logger = logger;
            _utcNow = utcNowProvider ?? (() => DateTime.UtcNow);
        }

        public async Task<ApiResponse<MobileAppDeviceStatusDto>> UpsertHeartbeatAsync(
            MobileAppDeviceHeartbeatDto dto,
            MobileAppDeviceHeartbeatAuthContext authContext
        )
        {
            var hardwareId = NormalizeText(dto.HardwareId, 120);
            if (string.IsNullOrWhiteSpace(hardwareId))
            {
                return ApiResponse<MobileAppDeviceStatusDto>.Error(
                    "缺少设备硬件识别码",
                    "INVALID_HARDWARE_ID"
                );
            }

            var now = EnsureUtc(_utcNow());
            var registeredDeviceResult = await ResolveRegisteredDeviceAsync(hardwareId);
            if (!registeredDeviceResult.Success)
            {
                return ApiResponse<MobileAppDeviceStatusDto>.Error(
                    "读取设备注册信息失败，心跳未写入",
                    "REGISTERED_DEVICE_LOOKUP_FAILED"
                );
            }

            var registeredDevice = registeredDeviceResult.Device;
            if (registeredDevice != null && !authContext.IsDeviceSessionVerified)
            {
                return ApiResponse<MobileAppDeviceStatusDto>.Error(
                    "已注册设备必须通过设备会话校验后才能更新 App 状态",
                    "REGISTERED_DEVICE_AUTH_REQUIRED"
                );
            }

            var existing = await _db
                .Queryable<MobileAppDeviceStatus>()
                .FirstAsync(item => item.HardwareId == hardwareId);
            var entity = existing ?? new MobileAppDeviceStatus { Id = Guid.NewGuid() };

            // 关键逻辑：只更新当前快照，不记录版本历史；已注册设备的分店和系统信息必须以服务端注册表为准。
            ApplyHeartbeat(entity, dto, hardwareId, registeredDevice, authContext, now);

            if (existing == null)
            {
                try
                {
                    await _db.Insertable(entity).ExecuteCommandAsync();
                }
                catch (Exception ex) when (IsUniqueHardwareIdConflict(ex))
                {
                    var concurrentExisting = await _db
                        .Queryable<MobileAppDeviceStatus>()
                        .FirstAsync(item => item.HardwareId == hardwareId);
                    if (concurrentExisting == null)
                    {
                        throw;
                    }

                    ApplyHeartbeat(concurrentExisting, dto, hardwareId, registeredDevice, authContext, now);
                    await _db.Updateable(concurrentExisting).ExecuteCommandAsync();
                    entity = concurrentExisting;
                }
            }
            else
            {
                await _db.Updateable(entity).ExecuteCommandAsync();
            }

            return ApiResponse<MobileAppDeviceStatusDto>.OK(
                MapToDto(entity, GetOnlineThreshold(now)),
                "心跳已记录"
            );
        }

        public async Task<ApiResponse<PagedResult<MobileAppDeviceStatusDto>>> GetPagedAsync(
            MobileAppDeviceStatusQueryDto query
        )
        {
            var page = Math.Max(query.Page, 1);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);
            var threshold = GetOnlineThreshold(EnsureUtc(_utcNow()));
            var queryable = BuildQuery(query, threshold);

            var total = await queryable.CountAsync();
            var items = await queryable
                .OrderByDescending(item => item.LastSeenAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return ApiResponse<PagedResult<MobileAppDeviceStatusDto>>.OK(
                new PagedResult<MobileAppDeviceStatusDto>
                {
                    Items = items.Select(item => MapToDto(item, threshold)).ToList(),
                    Total = total,
                    Page = page,
                    PageSize = pageSize,
                }
            );
        }

        public async Task<ApiResponse<MobileAppDeviceStatusSummaryDto>> GetSummaryAsync(
            MobileAppDeviceStatusQueryDto query
        )
        {
            var threshold = GetOnlineThreshold(EnsureUtc(_utcNow()));
            var allItems = await BuildQuery(
                    new MobileAppDeviceStatusQueryDto
                    {
                        StoreCode = query.StoreCode,
                        DeviceSystem = query.DeviceSystem,
                        Keyword = query.Keyword,
                    },
                    threshold
                )
                .ToListAsync();

            var online = allItems.Count(item => item.LastSeenAtUtc >= threshold);
            var android = allItems.Count(item => ResolveSystemBucket(item) == "android");
            var ios = allItems.Count(item => ResolveSystemBucket(item) == "ios");

            return ApiResponse<MobileAppDeviceStatusSummaryDto>.OK(
                new MobileAppDeviceStatusSummaryDto
                {
                    Total = allItems.Count,
                    Online = online,
                    Offline = allItems.Count - online,
                    Android = android,
                    Ios = ios,
                    UnknownSystem = allItems.Count - android - ios,
                }
            );
        }

        private ISugarQueryable<MobileAppDeviceStatus> BuildQuery(
            MobileAppDeviceStatusQueryDto query,
            DateTime onlineThreshold
        )
        {
            var queryable = _db
                .Queryable<MobileAppDeviceStatus>()
                .Where(item => !item.IsDeleted);

            var storeCode = NormalizeText(query.StoreCode, 50);
            if (!string.IsNullOrWhiteSpace(storeCode))
            {
                queryable = queryable.Where(item => item.StoreCode == storeCode);
            }

            var deviceSystem = NormalizeDeviceSystem(query.DeviceSystem);
            if (!string.IsNullOrWhiteSpace(deviceSystem))
            {
                queryable = queryable.Where(item => item.DeviceSystem == deviceSystem);
            }

            var keyword = NormalizeText(query.Keyword, 120);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                queryable = queryable.Where(item =>
                    item.HardwareId.Contains(keyword)
                    || (item.SystemDeviceNumber != null && item.SystemDeviceNumber.Contains(keyword))
                    || (item.StoreCode != null && item.StoreCode.Contains(keyword))
                    || (item.AppVersion != null && item.AppVersion.Contains(keyword))
                    || (item.RuntimeVersion != null && item.RuntimeVersion.Contains(keyword))
                    || (item.Channel != null && item.Channel.Contains(keyword))
                    || (item.UpdateId != null && item.UpdateId.Contains(keyword))
                    || (item.LastSeenUsername != null && item.LastSeenUsername.Contains(keyword))
                    || (item.LastSeenUserFullName != null && item.LastSeenUserFullName.Contains(keyword))
                );
            }

            var onlineState = NormalizeText(query.OnlineState, 30)?.ToLowerInvariant();
            queryable = onlineState switch
            {
                "online" => queryable.Where(item => item.LastSeenAtUtc >= onlineThreshold),
                "offline" => queryable.Where(item => item.LastSeenAtUtc < onlineThreshold),
                _ => queryable,
            };

            return queryable;
        }

        private async Task<(bool Success, POSM_设备注册信息表? Device)> ResolveRegisteredDeviceAsync(
            string hardwareId
        )
        {
            try
            {
                return (
                    true,
                    await _deviceRegistrationService.GetDeviceByHardwareIdAsync(hardwareId)
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取 POSM 注册设备信息失败，HardwareId: {HardwareId}", hardwareId);
                return (false, null);
            }
        }

        private static void ApplyHeartbeat(
            MobileAppDeviceStatus entity,
            MobileAppDeviceHeartbeatDto dto,
            string hardwareId,
            POSM_设备注册信息表? registeredDevice,
            MobileAppDeviceHeartbeatAuthContext authContext,
            DateTime now
        )
        {
            entity.HardwareId = hardwareId;
            entity.SystemDeviceNumber = FirstNonEmpty(
                NormalizeText(registeredDevice?.系统设备编号, 120),
                NormalizeText(dto.SystemDeviceNumber, 120)
            );
            entity.DeviceSystem = FirstNonEmpty(
                NormalizeDeviceSystem(registeredDevice?.设备系统),
                NormalizeDeviceSystem(dto.DeviceSystem),
                NormalizeDeviceSystem(dto.Platform)
            );
            entity.Platform = NormalizePlatform(dto.Platform);
            entity.StoreCode = FirstNonEmpty(
                NormalizeText(registeredDevice?.分店代码, 50),
                NormalizeText(dto.StoreCode, 50)
            );
            entity.AppVersion = NormalizeText(dto.AppVersion, 80);
            entity.AppBuildVersion = NormalizeText(dto.AppBuildVersion, 80);
            entity.RuntimeVersion = NormalizeText(dto.RuntimeVersion, 120);
            entity.Channel = NormalizeText(dto.Channel, 120);
            entity.UpdateId = NormalizeText(dto.UpdateId, 120);
            entity.UpdateSource = NormalizeText(dto.UpdateSource, 40);
            entity.LastSeenAtUtc = now;
            entity.LastAuthMode = NormalizeText(authContext.AuthMode, 30) ?? "unknown";
            entity.LastSeenUserGuid = NormalizeText(authContext.UserGuid, 50);
            entity.LastSeenUsername = NormalizeText(authContext.Username, 100);
            entity.LastSeenUserFullName = NormalizeText(authContext.UserFullName, 160);
            entity.RegisteredDeviceId = registeredDevice?.ID;
            entity.IsDeleted = false;
        }

        private static MobileAppDeviceStatusDto MapToDto(
            MobileAppDeviceStatus entity,
            DateTime onlineThreshold
        )
        {
            return new MobileAppDeviceStatusDto
            {
                Id = entity.Id,
                HardwareId = entity.HardwareId,
                SystemDeviceNumber = entity.SystemDeviceNumber,
                DeviceSystem = entity.DeviceSystem,
                Platform = entity.Platform,
                StoreCode = entity.StoreCode,
                AppVersion = entity.AppVersion,
                AppBuildVersion = entity.AppBuildVersion,
                RuntimeVersion = entity.RuntimeVersion,
                Channel = entity.Channel,
                UpdateId = entity.UpdateId,
                UpdateSource = entity.UpdateSource,
                LastSeenAtUtc = entity.LastSeenAtUtc,
                IsOnline = entity.LastSeenAtUtc >= onlineThreshold,
                LastAuthMode = entity.LastAuthMode,
                LastSeenUserGuid = entity.LastSeenUserGuid,
                LastSeenUsername = entity.LastSeenUsername,
                LastSeenUserFullName = entity.LastSeenUserFullName,
                RegisteredDeviceId = entity.RegisteredDeviceId,
            };
        }

        private static DateTime GetOnlineThreshold(DateTime now)
        {
            return now.AddMinutes(-OnlineWindowMinutes);
        }

        private static DateTime EnsureUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static bool IsUniqueHardwareIdConflict(Exception ex)
        {
            var message = ex.ToString();
            return message.Contains("MobileAppDeviceStatus", StringComparison.OrdinalIgnoreCase)
                && (
                    message.Contains("HardwareId", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("IX_MobileAppDeviceStatus_HardwareId", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                );
        }

        private static string ResolveSystemBucket(MobileAppDeviceStatus entity)
        {
            var normalized = NormalizeDeviceSystem(entity.DeviceSystem)
                ?? NormalizeDeviceSystem(entity.Platform);
            return normalized switch
            {
                "Android" => "android",
                "iOS" => "ios",
                _ => "unknown",
            };
        }

        private static string? NormalizeDeviceSystem(string? value)
        {
            var normalized = NormalizeText(value, 30);
            if (normalized == null)
            {
                return null;
            }

            return normalized.ToLowerInvariant() switch
            {
                "android" => "Android",
                "ios" => "iOS",
                "iphone" => "iOS",
                "ipad" => "iOS",
                _ => normalized,
            };
        }

        private static string? NormalizePlatform(string? value)
        {
            var normalized = NormalizeText(value, 30);
            return normalized?.ToLowerInvariant() switch
            {
                "android" => "android",
                "ios" => "ios",
                _ => normalized,
            };
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        private static string? NormalizeText(string? value, int maxLength)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }

            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }
    }

    public sealed record MobileAppDeviceHeartbeatAuthContext(
        string AuthMode,
        string? UserGuid,
        string? Username,
        string? UserFullName,
        bool IsDeviceSessionVerified = false
    );
}
