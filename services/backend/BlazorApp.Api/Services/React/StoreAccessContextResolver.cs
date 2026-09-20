using System.Diagnostics;
using System.Security.Claims;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    /// <summary>商品维护类接口的访问上下文：登录用户按分店范围，匿名绑定设备按设备所属分店。</summary>
    public sealed class StoreAccessContext
    {
        public bool IsAllowed { get; set; }
        public string Message { get; set; } = "未授权";
        public string ActorLabel { get; set; } = "system";
        /// <summary>null 表示全部分店（管理角色），空列表表示无任何分店。</summary>
        public List<string>? StoreCodes { get; set; }
        public long AccessResolveMs { get; set; }
        public long UserLookupMs { get; set; }
        public long UserStoreScopeMs { get; set; }
        public long DeviceAuthMs { get; set; }
        public long DeviceLoadMs { get; set; }

        public bool CanAccessStore(string? storeCode)
        {
            if (!IsAllowed)
            {
                return false;
            }

            if (StoreCodes == null)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(storeCode) && StoreCodes.Contains(storeCode.Trim());
        }
    }

    /// <summary>
    /// 从 ReactStoreProductMaintenanceController 抽出的访问上下文解析，供商品维护与离线目录控制器共用。
    /// 逻辑保持原样：登录用户（管理角色全店；否则按 UserStore 范围）→ 设备头（X-Device-Id / X-Auth-Code）。
    /// </summary>
    public sealed class StoreAccessContextResolver
    {
        private readonly ISqlSugarClient _db;
        private readonly IDeviceRegistrationService _deviceRegistrationService;
        private readonly IMapper _mapper;
        private readonly ILogger<StoreAccessContextResolver> _logger;

        public StoreAccessContextResolver(
            SqlSugarContext context,
            IDeviceRegistrationService deviceRegistrationService,
            IMapper mapper,
            ILogger<StoreAccessContextResolver> logger)
        {
            _db = context.Db;
            _deviceRegistrationService = deviceRegistrationService;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<StoreAccessContext> ResolveAsync(ClaimsPrincipal? user, IHeaderDictionary headers)
        {
            var sw = Stopwatch.StartNew();
            if (user?.Identity?.IsAuthenticated == true)
            {
                try
                {
                    if (HasElevatedStoreAccess(user))
                    {
                        return new StoreAccessContext
                        {
                            IsAllowed = true,
                            ActorLabel = user.Identity?.Name ?? "system",
                            StoreCodes = null,
                            AccessResolveMs = sw.ElapsedMilliseconds,
                        };
                    }

                    var userLookupSw = Stopwatch.StartNew();
                    var userGuid = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    var actorLabel = user.Identity?.Name ?? "system";
                    if (string.IsNullOrWhiteSpace(userGuid) && !string.IsNullOrWhiteSpace(actorLabel))
                    {
                        userGuid = await _db.Queryable<User>()
                            .Where(u => u.Username == actorLabel && !u.IsDeleted)
                            .Select(u => u.UserGUID)
                            .FirstAsync();
                    }
                    userLookupSw.Stop();

                    if (string.IsNullOrWhiteSpace(userGuid))
                    {
                        return new StoreAccessContext
                        {
                            IsAllowed = false,
                            Message = "未找到当前用户信息",
                            AccessResolveMs = sw.ElapsedMilliseconds,
                            UserLookupMs = userLookupSw.ElapsedMilliseconds,
                        };
                    }

                    var userStoreScopeSw = Stopwatch.StartNew();
                    var storeCodes = await _db.Queryable<UserStore>()
                        .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
                        .Where((us, s) => us.UserGUID == userGuid && !us.IsDeleted && !s.IsDeleted)
                        .Select((us, s) => s.StoreCode)
                        .ToListAsync();
                    userStoreScopeSw.Stop();

                    return new StoreAccessContext
                    {
                        IsAllowed = true,
                        ActorLabel = actorLabel,
                        StoreCodes = storeCodes,
                        AccessResolveMs = sw.ElapsedMilliseconds,
                        UserLookupMs = userLookupSw.ElapsedMilliseconds,
                        UserStoreScopeMs = userStoreScopeSw.ElapsedMilliseconds,
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "解析登录用户可访问分店失败");
                    return new StoreAccessContext
                    {
                        IsAllowed = false,
                        Message = "解析当前用户分店权限失败",
                        AccessResolveMs = sw.ElapsedMilliseconds,
                    };
                }
            }

            var hardwareId = headers["X-Device-Id"].FirstOrDefault();
            var authCode = headers["X-Auth-Code"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(hardwareId) || string.IsNullOrWhiteSpace(authCode))
            {
                return new StoreAccessContext
                {
                    IsAllowed = false,
                    Message = "未登录且缺少设备授权信息",
                    AccessResolveMs = sw.ElapsedMilliseconds,
                };
            }

            var deviceAuthSw = Stopwatch.StartNew();
            var isValid = await _deviceRegistrationService.ValidateDeviceAuthCodeAsync(hardwareId, authCode);
            deviceAuthSw.Stop();
            if (!isValid)
            {
                return new StoreAccessContext
                {
                    IsAllowed = false,
                    Message = "设备授权无效",
                    AccessResolveMs = sw.ElapsedMilliseconds,
                    DeviceAuthMs = deviceAuthSw.ElapsedMilliseconds,
                };
            }

            var deviceLoadSw = Stopwatch.StartNew();
            var deviceEntity = await _deviceRegistrationService.GetDeviceByHardwareIdAsync(hardwareId);
            deviceLoadSw.Stop();
            if (deviceEntity == null)
            {
                return new StoreAccessContext
                {
                    IsAllowed = false,
                    Message = "设备不存在",
                    AccessResolveMs = sw.ElapsedMilliseconds,
                    DeviceAuthMs = deviceAuthSw.ElapsedMilliseconds,
                    DeviceLoadMs = deviceLoadSw.ElapsedMilliseconds,
                };
            }

            var device = _mapper.Map<DeviceDataDto>(deviceEntity);
            if (device.Status != 1 || string.IsNullOrWhiteSpace(device.StoreCode))
            {
                return new StoreAccessContext
                {
                    IsAllowed = false,
                    Message = "设备未启用或未绑定分店",
                    AccessResolveMs = sw.ElapsedMilliseconds,
                    DeviceAuthMs = deviceAuthSw.ElapsedMilliseconds,
                    DeviceLoadMs = deviceLoadSw.ElapsedMilliseconds,
                };
            }

            return new StoreAccessContext
            {
                IsAllowed = true,
                ActorLabel = $"device:{device.HardwareId}",
                StoreCodes = new List<string> { device.StoreCode },
                AccessResolveMs = sw.ElapsedMilliseconds,
                DeviceAuthMs = deviceAuthSw.ElapsedMilliseconds,
                DeviceLoadMs = deviceLoadSw.ElapsedMilliseconds,
            };
        }

        public static bool HasElevatedStoreAccess(ClaimsPrincipal user)
        {
            return HasSuperAdminRole(user)
                || HasRole(user, "Manager")
                || HasRole(user, "WarehouseManager")
                || HasRole(user, "WarehouseStaff");
        }

        private static bool HasSuperAdminRole(ClaimsPrincipal user)
        {
            return user.Claims.Any(claim =>
                claim.Type == ClaimTypes.Role
                && Permissions.IsSuperAdminRole(claim.Value)
            );
        }

        private static bool HasRole(ClaimsPrincipal user, string role)
        {
            return user.Claims.Any(claim =>
                claim.Type == ClaimTypes.Role
                && claim.Value.Equals(role, StringComparison.OrdinalIgnoreCase)
            );
        }

        public static string FormatStoreScope(List<string>? storeCodes)
        {
            if (storeCodes == null)
            {
                return "ALL";
            }

            return storeCodes.Count == 0 ? "NONE" : string.Join(",", storeCodes);
        }
    }
}
