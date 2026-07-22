using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using SqlSugar;

namespace BlazorApp.Api.Services;

/// <summary>
/// 把 WPF 更新的严格设备认证、定向策略和后台选择项集中在独立服务中，避免改变通用设备认证的兼容语义。
/// </summary>
public sealed class WpfAppReleaseTargetingService(
    WpfAppReleaseService releaseService,
    SqlSugarContext mainContext,
    POSMSqlSugarContext posmContext
) : IWpfAppReleaseTargetingService
{
    public async Task<ApiResponse<WpfUpdatePolicyDto>> SetPolicyAsync(
        WpfUpdatePolicyRequest request,
        string currentUser
    )
    {
        var targetScope = NormalizeOptional(request.TargetScope)?.ToLowerInvariant() ?? "all";
        if (targetScope == "stores")
        {
            var requestedStoreGuids = request.TargetStoreGuids
                .Select(NormalizeOptional)
                .Where(value => value is not null)
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (requestedStoreGuids.Count > 0)
            {
                var activeStores = await mainContext.Db.Queryable<Store>()
                    .Where(store => store.IsActive && !store.IsDeleted)
                    .ToListAsync();
                var storesByGuid = activeStores.ToDictionary(
                    store => store.StoreGUID,
                    StringComparer.OrdinalIgnoreCase
                );
                if (requestedStoreGuids.Any(guid => !storesByGuid.ContainsKey(guid)))
                {
                    return ApiResponse<WpfUpdatePolicyDto>.Error(
                        "Every target store must be active and available.",
                        "TARGET_STORES_INVALID"
                    );
                }

                // 使用数据库的 canonical StoreGUID，保证大小写变体不会保存成不可匹配的旧值。
                request.TargetStoreGuids = requestedStoreGuids
                    .Select(guid => storesByGuid[guid].StoreGUID)
                    .ToList();
            }
        }
        else if (targetScope == "devices")
        {
            var requestedDeviceIds = request.TargetDeviceRegistrationIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (requestedDeviceIds.Count > 0)
            {
                var devices = await posmContext.Db.Queryable<POSM_设备注册信息表>()
                    .Where(device => requestedDeviceIds.Contains(device.ID))
                    .ToListAsync();
                var hardwareIds = devices
                    .Select(device => device.设备硬件识别码)
                    .Where(hardwareId => !string.IsNullOrWhiteSpace(hardwareId))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var latestDeviceIds = await GetLatestDeviceRegistrationIdsAsync(hardwareIds);
                var validDeviceIds = devices
                    .Where(device =>
                        !string.IsNullOrWhiteSpace(device.设备硬件识别码)
                        && latestDeviceIds.Contains(device.ID)
                        && device.设备状态 == (int)DeviceStatus.启用
                        && string.Equals(device.设备类型, "POS", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(device.设备系统, "Windows", StringComparison.OrdinalIgnoreCase)
                    )
                    .Select(device => device.ID)
                    .ToHashSet();
                if (requestedDeviceIds.Any(id => !validDeviceIds.Contains(id)))
                {
                    return ApiResponse<WpfUpdatePolicyDto>.Error(
                        "Every target device must be an enabled Windows POS device.",
                        "TARGET_DEVICES_INVALID"
                    );
                }
            }
        }

        return await releaseService.SetTargetedPolicyAsync(request, currentUser);
    }

    public async Task<ApiResponse<WpfUpdateCheckResponse>> CheckUpdateAsync(
        string? channel,
        string? currentVersion,
        string? deviceId,
        string? authCode
    )
    {
        var identity = await ResolveStrictDeviceIdentityAsync(deviceId, authCode);
        return await releaseService.CheckTargetedUpdateAsync(channel, currentVersion, identity);
    }

    public async Task<ApiResponse<WpfUpdateTargetStoreOptionsResponse>> GetStoreOptionsAsync()
    {
        var items = await mainContext.Db.Queryable<Store>()
            .Where(store => store.IsActive && !store.IsDeleted)
            .OrderBy(store => store.StoreCode)
            .Select(store => new WpfUpdateTargetStoreOptionDto
            {
                StoreGuid = store.StoreGUID,
                StoreCode = store.StoreCode,
                StoreName = store.StoreName,
            })
            .ToListAsync();

        return ApiResponse<WpfUpdateTargetStoreOptionsResponse>.OK(
            new WpfUpdateTargetStoreOptionsResponse { Items = items }
        );
    }

    public async Task<ApiResponse<PagedResult<WpfUpdateTargetDeviceOptionDto>>> GetDeviceOptionsAsync(
        int page,
        int pageSize,
        string? keyword
    )
    {
        var normalizedKeyword = NormalizeOptional(keyword);
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var query = posmContext.Db.Queryable<POSM_设备注册信息表>()
            .Where(device =>
                device.设备硬件识别码.Trim() != string.Empty
                && !SqlFunc.Subqueryable<POSM_设备注册信息表>()
                    .Where(newerDevice =>
                        newerDevice.设备硬件识别码 == device.设备硬件识别码
                        && newerDevice.ID > device.ID)
                    .Any()
                && device.设备状态 == (int)DeviceStatus.启用
                && device.设备类型 == "POS"
                && device.设备系统 == "Windows"
            );
        if (normalizedKeyword is not null)
        {
            query = query.Where(device =>
                device.系统设备编号.Contains(normalizedKeyword)
                || (device.分店代码 ?? string.Empty).Contains(normalizedKeyword)
                || (device.备注 ?? string.Empty).Contains(normalizedKeyword)
            );
        }

        var total = await query.CountAsync();
        var devices = await query
            .OrderByDescending(device => device.创建时间)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();
        var storeCodes = devices
            .Select(device => NormalizeOptional(device.分店代码))
            .Where(code => code is not null)
            .Select(code => code!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var stores = storeCodes.Count == 0
            ? new List<Store>()
            : await mainContext.Db.Queryable<Store>()
                .Where(store => storeCodes.Contains(store.StoreCode) && !store.IsDeleted)
                .ToListAsync();
        var storeNames = stores.ToDictionary(
            store => store.StoreCode,
            store => store.StoreName,
            StringComparer.OrdinalIgnoreCase
        );

        // 目标选择接口只返回注册主键和展示信息，绝不向后台界面泄露授权码或完整硬件识别码。
        var items = devices.Select(device => new WpfUpdateTargetDeviceOptionDto
        {
            DeviceRegistrationId = device.ID,
            SystemDeviceNumber = device.系统设备编号,
            StoreCode = device.分店代码,
            StoreName = NormalizeOptional(device.分店代码) is { } storeCode
                && storeNames.TryGetValue(storeCode, out var storeName)
                    ? storeName
                    : null,
            Remarks = device.备注,
        }).ToList();

        return ApiResponse<PagedResult<WpfUpdateTargetDeviceOptionDto>>.OK(
            new PagedResult<WpfUpdateTargetDeviceOptionDto>
            {
                Items = items,
                Total = total,
                Page = normalizedPage,
                PageSize = normalizedPageSize,
            }
        );
    }

    private async Task<WpfUpdateCheckDeviceIdentity?> ResolveStrictDeviceIdentityAsync(
        string? deviceId,
        string? authCode
    )
    {
        var normalizedDeviceId = NormalizeOptional(deviceId);
        var normalizedAuthCode = NormalizeOptional(authCode);
        if (normalizedDeviceId is null || normalizedAuthCode is null)
        {
            return null;
        }

        // 只按 hardwareId 取全局最新登记记录，再在内存逐字段校验。若先按旧授权码、状态等过滤，
        // 同一硬件换码或禁用后会错误回退命中较旧的启用记录。
        var device = await posmContext.Db.Queryable<POSM_设备注册信息表>()
            .Where(item => item.设备硬件识别码 == normalizedDeviceId)
            .OrderByDescending(item => item.ID)
            .FirstAsync();
        if (
            device == null
            || !string.Equals(device.设备硬件识别码, normalizedDeviceId, StringComparison.Ordinal)
            || !string.Equals(device.设备授权码, normalizedAuthCode, StringComparison.Ordinal)
            || device.设备状态 != (int)DeviceStatus.启用
            || !string.Equals(device.设备类型, "POS", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(device.设备系统, "Windows", StringComparison.OrdinalIgnoreCase)
        )
        {
            return null;
        }

        return new WpfUpdateCheckDeviceIdentity
        {
            DeviceRegistrationId = device.ID,
            StoreCode = NormalizeOptional(device.分店代码),
        };
    }

    private async Task<HashSet<int>> GetLatestDeviceRegistrationIdsAsync(
        List<string>? hardwareIds = null
    )
    {
        var query = posmContext.Db.Queryable<POSM_设备注册信息表>()
            .Where(device => device.设备硬件识别码.Trim() != string.Empty);
        if (hardwareIds is not null)
        {
            query = query.Where(device => hardwareIds.Contains(device.设备硬件识别码));
        }

        var latestDeviceIds = await query
            .GroupBy(device => device.设备硬件识别码)
            .Select(device => SqlFunc.AggregateMax(device.ID))
            .ToListAsync();
        return latestDeviceIds.ToHashSet();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

}
