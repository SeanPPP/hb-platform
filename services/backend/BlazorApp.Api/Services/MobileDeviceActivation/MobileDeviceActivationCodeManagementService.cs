using System.Linq.Expressions;
using System.Security.Claims;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using BlazorApp.Shared.Security;
using SqlSugar;

namespace BlazorApp.Api.Services.MobileDeviceActivation;

public interface IMobileDeviceActivationCodeManagementService
{
    Task<ApiResponse<List<MobileDeviceActivationManageableStoreDto>>> GetManageableStoresAsync();

    Task<ApiResponse<List<MobileDeviceActivationManageableAccountDto>>> GetManageableAccountsAsync(
        string? storeCode);

    Task<ApiResponse<PagedResult<MobileDeviceActivationGrantDto>>> ListAsync(
        int page,
        int pageSize,
        string? storeCode,
        string? deviceSystem,
        string? status);

    Task<ApiResponse<MobileDeviceActivationCodeCreateResponseDto>> CreateAsync(
        MobileDeviceActivationCodeCreateRequestDto request,
        string actor);

    Task<ApiResponse<MobileDeviceActivationGrantDto>> RevokeAsync(
        Guid grantId,
        MobileDeviceActivationCodeRevokeRequestDto request,
        string actor);
}

public sealed class MobileDeviceActivationCodeManagementService :
    IMobileDeviceActivationCodeManagementService
{
    private static readonly HashSet<int> AllowedValidForMinutes = [30, 120, 1440];
    private static readonly Expression<Func<MobileDeviceActivationGrant, MobileDeviceActivationGrant>>
        ManagementProjection = grant => new MobileDeviceActivationGrant
        {
            GrantId = grant.GrantId,
            StoreCode = grant.StoreCode,
            DeviceSystem = grant.DeviceSystem,
            TargetUserGuid = grant.TargetUserGuid,
            TargetUsernameSnapshot = grant.TargetUsernameSnapshot,
            TargetFullNameSnapshot = grant.TargetFullNameSnapshot,
            CreatedAtUtc = grant.CreatedAtUtc,
            CreatedBy = grant.CreatedBy,
            Reason = grant.Reason,
            ExpiresAtUtc = grant.ExpiresAtUtc,
            RevokedAtUtc = grant.RevokedAtUtc,
            RevokedBy = grant.RevokedBy,
            RevokeReason = grant.RevokeReason,
            ConsumedAtUtc = grant.ConsumedAtUtc,
            ConsumedHardwareId = grant.ConsumedHardwareId,
            ConsumedDeviceCode = grant.ConsumedDeviceCode,
            ConsumptionKind = grant.ConsumptionKind,
        };

    private readonly ISqlSugarClient _posmDb;
    private readonly ISqlSugarClient _mainDb;
    private readonly ICurrentUserManageableStoreScopeService _scopeService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MobileDeviceActivationCodeManagementService> _logger;
    private readonly TimeProvider _timeProvider;

    public MobileDeviceActivationCodeManagementService(
        POSMSqlSugarContext posmContext,
        SqlSugarContext mainContext,
        ICurrentUserManageableStoreScopeService scopeService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<MobileDeviceActivationCodeManagementService> logger,
        TimeProvider? timeProvider = null)
        : this(
            posmContext.Db,
            mainContext.Db,
            scopeService,
            logger,
            timeProvider,
            httpContextAccessor)
    {
    }

    public MobileDeviceActivationCodeManagementService(
        ISqlSugarClient posmDb,
        ISqlSugarClient mainDb,
        ICurrentUserManageableStoreScopeService scopeService,
        ILogger<MobileDeviceActivationCodeManagementService> logger,
        TimeProvider? timeProvider = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _posmDb = posmDb;
        _mainDb = mainDb;
        _scopeService = scopeService;
        _httpContextAccessor = httpContextAccessor ?? new HttpContextAccessor();
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ApiResponse<List<MobileDeviceActivationManageableStoreDto>>>
        GetManageableStoresAsync()
    {
        var scope = await _scopeService.GetScopeAsync();
        if (!scope.IsAllowed)
        {
            return ApiResponse<List<MobileDeviceActivationManageableStoreDto>>.Error(
                ScopeMessage(scope),
                "MOBILE_ACTIVATION_STORE_SCOPE_FORBIDDEN");
        }

        var accessibleCodes = scope.StoreCodes.ToList();
        var query = _mainDb.Queryable<Store>()
            .Where(store => store.IsActive && !store.IsDeleted);
        if (!scope.IsAdmin)
        {
            query = query.Where(store => accessibleCodes.Contains(store.StoreCode));
        }

        var stores = await query
            .OrderBy(store => store.StoreCode)
            .Select(store => new { store.StoreCode, store.StoreName })
            .ToListAsync();
        return ApiResponse<List<MobileDeviceActivationManageableStoreDto>>.OK(
            stores.Select(store => new MobileDeviceActivationManageableStoreDto(
                Redact(store.StoreCode),
                Redact(store.StoreName))).ToList());
    }

    public async Task<ApiResponse<List<MobileDeviceActivationManageableAccountDto>>>
        GetManageableAccountsAsync(string? storeCode)
    {
        var normalizedStoreCode = Normalize(storeCode);
        if (normalizedStoreCode == null)
        {
            return ApiResponse<List<MobileDeviceActivationManageableAccountDto>>.Error(
                "storeCode 不能为空",
                "MOBILE_ACTIVATION_STORE_REQUIRED");
        }

        var scope = await _scopeService.GetScopeAsync();
        if (!scope.IsAllowed || !scope.CanAccessStoreCode(normalizedStoreCode))
        {
            return ApiResponse<List<MobileDeviceActivationManageableAccountDto>>.Error(
                "无权管理该分店的 Mobile 设备开通码",
                "MOBILE_ACTIVATION_STORE_FORBIDDEN");
        }

        var store = await _mainDb.Queryable<Store>()
            .Where(item => item.StoreCode == normalizedStoreCode
                && item.IsActive
                && !item.IsDeleted)
            .Select(item => new Store
            {
                StoreGUID = item.StoreGUID,
                StoreCode = item.StoreCode,
            })
            .FirstAsync();
        if (store == null)
        {
            return ApiResponse<List<MobileDeviceActivationManageableAccountDto>>.Error(
                "分店不存在或已停用",
                "MOBILE_ACTIVATION_STORE_UNAVAILABLE");
        }

        var users = await MobileDeviceActivationQueries
            .BuildManageableAccountsQuery(_mainDb, store.StoreGUID)
            .ToListAsync();

        if (!IsTrueAdministrator() && users.Count > 0)
        {
            var userGuids = users.Select(user => user.UserGUID).ToList();
            var privilegedUsers = await _mainDb.Queryable<UserRole>()
                .InnerJoin<Role>((userRole, role) => userRole.RoleGUID == role.RoleGUID)
                .Where((userRole, role) =>
                    userGuids.Contains(userRole.UserGUID)
                    && !userRole.IsDeleted
                    && !role.IsDeleted
                    && role.IsActive)
                .Select((userRole, role) => new
                {
                    userRole.UserGUID,
                    role.RoleName,
                })
                .ToListAsync();
            var deniedUserGuids = privilegedUsers
                .Where(item => Permissions.SuperAdminRoleNames.Any(role =>
                    role.Equals(item.RoleName, StringComparison.OrdinalIgnoreCase)))
                .Select(item => item.UserGUID)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            users = users.Where(user => !deniedUserGuids.Contains(user.UserGUID)).ToList();
        }

        return ApiResponse<List<MobileDeviceActivationManageableAccountDto>>.OK(
            users
                .OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
                .Select(user => new MobileDeviceActivationManageableAccountDto(
                    Redact(user.UserGUID),
                    Redact(user.Username),
                    RedactNullable(user.FullName)))
                .ToList());
    }

    public async Task<ApiResponse<PagedResult<MobileDeviceActivationGrantDto>>> ListAsync(
        int page,
        int pageSize,
        string? storeCode,
        string? deviceSystem,
        string? status)
    {
        var scope = await _scopeService.GetScopeAsync();
        if (!scope.IsAllowed)
        {
            return ApiResponse<PagedResult<MobileDeviceActivationGrantDto>>.Error(
                ScopeMessage(scope),
                "MOBILE_ACTIVATION_STORE_SCOPE_FORBIDDEN");
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var normalizedStore = Normalize(storeCode);
        var normalizedStatus = Normalize(status);
        if (normalizedStore != null && !scope.CanAccessStoreCode(normalizedStore))
        {
            return ApiResponse<PagedResult<MobileDeviceActivationGrantDto>>.Error(
                "无权查看该分店的 Mobile 设备开通码",
                "MOBILE_ACTIVATION_STORE_FORBIDDEN");
        }

        string? normalizedSystem = null;
        if (Normalize(deviceSystem) != null
            && !MobileDeviceActivationRules.TryNormalizeDeviceSystem(deviceSystem, out normalizedSystem))
        {
            return ApiResponse<PagedResult<MobileDeviceActivationGrantDto>>.Error(
                "deviceSystem 无效",
                "MOBILE_ACTIVATION_SYSTEM_INVALID");
        }

        var accessibleStores = scope.StoreCodes.ToList();
        var query = _posmDb.Queryable<MobileDeviceActivationGrant>();
        if (!scope.IsAdmin)
        {
            query = query.Where(grant => accessibleStores.Contains(grant.StoreCode));
        }
        if (normalizedStore != null)
        {
            query = query.Where(grant => grant.StoreCode == normalizedStore);
        }
        if (normalizedSystem != null)
        {
            query = query.Where(grant => grant.DeviceSystem == normalizedSystem);
        }

        var now = UtcNow();
        query = normalizedStatus switch
        {
            "Available" => query.Where(grant => grant.ConsumedAtUtc == null
                && grant.RevokedAtUtc == null
                && grant.ExpiresAtUtc > now),
            "Consumed" => query.Where(grant => grant.ConsumedAtUtc != null),
            "Revoked" => query.Where(grant => grant.ConsumedAtUtc == null
                && grant.RevokedAtUtc != null),
            "Expired" => query.Where(grant => grant.ConsumedAtUtc == null
                && grant.RevokedAtUtc == null
                && grant.ExpiresAtUtc <= now),
            null => query,
            _ => null!,
        };
        if (query == null)
        {
            return ApiResponse<PagedResult<MobileDeviceActivationGrantDto>>.Error(
                "status 无效",
                "MOBILE_ACTIVATION_STATUS_INVALID");
        }

        var total = await query.CountAsync();
        var grants = await query
            .OrderByDescending(grant => grant.CreatedAtUtc)
            .Select(ManagementProjection)
            .ToPageListAsync(page, pageSize);
        var storeNames = await LoadStoreNamesAsync(grants.Select(grant => grant.StoreCode));
        return ApiResponse<PagedResult<MobileDeviceActivationGrantDto>>.OK(
            new PagedResult<MobileDeviceActivationGrantDto>
            {
                Items = grants.Select(grant => Map(grant, storeNames, now)).ToList(),
                Total = total,
                Page = page,
                PageSize = pageSize,
            });
    }

    public async Task<ApiResponse<MobileDeviceActivationCodeCreateResponseDto>> CreateAsync(
        MobileDeviceActivationCodeCreateRequestDto request,
        string actor)
    {
        var storeCode = Normalize(request.StoreCode);
        var targetUserGuid = Normalize(request.TargetUserGuid);
        var reason = NormalizeReason(request.Reason);
        if (storeCode == null)
        {
            return CreateError("storeCode 不能为空", "MOBILE_ACTIVATION_STORE_REQUIRED");
        }
        if (!MobileDeviceActivationRules.TryNormalizeDeviceSystem(
                request.DeviceSystem,
                out var deviceSystem))
        {
            return CreateError("deviceSystem 仅允许 Android 或 iOS", "MOBILE_ACTIVATION_SYSTEM_INVALID");
        }
        if (targetUserGuid == null)
        {
            return CreateError("targetUserGuid 不能为空", "MOBILE_ACTIVATION_ACCOUNT_REQUIRED");
        }
        if (!AllowedValidForMinutes.Contains(request.ValidForMinutes))
        {
            return CreateError("validForMinutes 仅允许 30、120 或 1440", "MOBILE_ACTIVATION_VALIDITY_INVALID");
        }
        if (reason == null)
        {
            return CreateError("reason 不能为空且不能超过 200 个字符", "MOBILE_ACTIVATION_REASON_INVALID");
        }

        var accounts = await GetManageableAccountsAsync(storeCode);
        var target = accounts.Success
            ? accounts.Data?.FirstOrDefault(account => account.UserGuid.Equals(
                targetUserGuid,
                StringComparison.OrdinalIgnoreCase))
            : null;
        if (!accounts.Success)
        {
            return CreateError(accounts.Message, accounts.ErrorCode ?? "MOBILE_ACTIVATION_STORE_FORBIDDEN");
        }
        if (target == null)
        {
            return CreateError(
                "目标账号不存在、已停用或不在可管理范围内",
                "MOBILE_ACTIVATION_ACCOUNT_UNAVAILABLE");
        }

        var material = DeviceActivationCodeCodec.Create();
        var now = UtcNow();
        var grant = new MobileDeviceActivationGrant
        {
            GrantId = material.GrantId,
            SecretHash = material.SecretHash,
            StoreCode = storeCode,
            DeviceSystem = deviceSystem,
            TargetUserGuid = target.UserGuid,
            TargetUsernameSnapshot = target.Username,
            TargetFullNameSnapshot = target.FullName,
            CreatedAtUtc = now,
            CreatedBy = NormalizeActor(actor),
            Reason = reason,
            ExpiresAtUtc = now.AddMinutes(request.ValidForMinutes),
        };
        await _posmDb.Insertable(grant).ExecuteCommandAsync();

        _logger.LogInformation(
            "已创建 Mobile 设备开通码摘要，GrantId={GrantId}, StoreCode={StoreCode}, TargetUserGuid={TargetUserGuid}, DeviceSystem={DeviceSystem}",
            grant.GrantId,
            grant.StoreCode,
            grant.TargetUserGuid,
            grant.DeviceSystem);
        var storeNames = await LoadStoreNamesAsync([storeCode]);
        return ApiResponse<MobileDeviceActivationCodeCreateResponseDto>.OK(
            new MobileDeviceActivationCodeCreateResponseDto(
                Map(grant, storeNames, now),
                material.ActivationCode),
            "Mobile 设备开通码已创建；明文仅显示本次");
    }

    public async Task<ApiResponse<MobileDeviceActivationGrantDto>> RevokeAsync(
        Guid grantId,
        MobileDeviceActivationCodeRevokeRequestDto request,
        string actor)
    {
        var reason = NormalizeReason(request.Reason);
        if (grantId == Guid.Empty || reason == null)
        {
            return ApiResponse<MobileDeviceActivationGrantDto>.Error(
                "grantId 或 reason 无效",
                "MOBILE_ACTIVATION_REVOKE_INVALID");
        }

        var grant = await _posmDb.Queryable<MobileDeviceActivationGrant>()
            .Where(item => item.GrantId == grantId)
            .Select(item => new MobileDeviceActivationGrant
            {
                GrantId = item.GrantId,
                StoreCode = item.StoreCode,
            })
            .FirstAsync();
        if (grant == null)
        {
            return ApiResponse<MobileDeviceActivationGrantDto>.Error(
                "Mobile 设备开通码不存在",
                "MOBILE_ACTIVATION_GRANT_NOT_FOUND");
        }
        if (!await _scopeService.CanAccessStoreCodeAsync(grant.StoreCode))
        {
            return ApiResponse<MobileDeviceActivationGrantDto>.Error(
                "无权管理该分店的 Mobile 设备开通码",
                "MOBILE_ACTIVATION_STORE_FORBIDDEN");
        }

        var revokedAtUtc = UtcNow();
        var revokedBy = NormalizeActor(actor);
        var affected = await _posmDb.Updateable<MobileDeviceActivationGrant>()
            .SetColumns(item => new MobileDeviceActivationGrant
            {
                RevokedAtUtc = revokedAtUtc,
                RevokedBy = revokedBy,
                RevokeReason = reason,
            })
            .Where(item => item.GrantId == grantId
                && item.RevokedAtUtc == null
                && item.ConsumedAtUtc == null)
            .ExecuteCommandAsync();
        if (affected != 1)
        {
            return ApiResponse<MobileDeviceActivationGrantDto>.Error(
                "Mobile 设备开通码已使用或已撤销",
                "MOBILE_ACTIVATION_NOT_REVOCABLE");
        }

        var updated = await _posmDb.Queryable<MobileDeviceActivationGrant>()
            .Where(item => item.GrantId == grantId)
            .Select(ManagementProjection)
            .FirstAsync()
            ?? throw new InvalidOperationException("Revoked mobile activation grant disappeared.");
        var storeNames = await LoadStoreNamesAsync([updated.StoreCode]);
        return ApiResponse<MobileDeviceActivationGrantDto>.OK(
            Map(updated, storeNames, UtcNow()),
            "Mobile 设备开通码已撤销");
    }

    private async Task<Dictionary<string, string>> LoadStoreNamesAsync(
        IEnumerable<string> storeCodes)
    {
        var codes = storeCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (codes.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var stores = await _mainDb.Queryable<Store>()
            .Where(store => codes.Contains(store.StoreCode))
            .Select(store => new { store.StoreCode, store.StoreName })
            .ToListAsync();
        return stores.ToDictionary(
            store => store.StoreCode,
            store => store.StoreName,
            StringComparer.OrdinalIgnoreCase);
    }

    private static MobileDeviceActivationGrantDto Map(
        MobileDeviceActivationGrant grant,
        IReadOnlyDictionary<string, string> storeNames,
        DateTime now)
    {
        var status = grant.ConsumedAtUtc != null
            ? "Consumed"
            : grant.RevokedAtUtc != null
                ? "Revoked"
                : grant.ExpiresAtUtc <= now
                    ? "Expired"
                    : "Available";
        storeNames.TryGetValue(grant.StoreCode, out var storeName);
        return new MobileDeviceActivationGrantDto(
            grant.GrantId,
            Redact(grant.StoreCode),
            RedactNullable(storeName),
            Redact(grant.DeviceSystem),
            status,
            Redact(grant.TargetUserGuid),
            Redact(grant.TargetUsernameSnapshot),
            RedactNullable(grant.TargetFullNameSnapshot),
            DeviceActivationCodeCodec.NormalizeUtcForWire(grant.CreatedAtUtc),
            Redact(grant.CreatedBy),
            Redact(grant.Reason),
            DeviceActivationCodeCodec.NormalizeUtcForWire(grant.ExpiresAtUtc),
            DeviceActivationCodeCodec.NormalizeUtcForWire(grant.RevokedAtUtc),
            RedactNullable(grant.RevokedBy),
            RedactNullable(grant.RevokeReason),
            DeviceActivationCodeCodec.NormalizeUtcForWire(grant.ConsumedAtUtc),
            RedactNullable(grant.ConsumedHardwareId),
            RedactNullable(grant.ConsumedDeviceCode),
            RedactNullable(grant.ConsumptionKind));
    }

    private static string ScopeMessage(CurrentUserManageableStoreScope scope) =>
        scope.Message.Length > 0 ? scope.Message : "当前账号没有可管理分店";

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string? NormalizeReason(string? value)
    {
        var normalized = Normalize(value);
        return normalized is { Length: <= 200 }
            && !DeviceActivationCodeCodec.ContainsReservedActivationCode(normalized)
            ? normalized
            : null;
    }

    private static string NormalizeActor(string? value)
    {
        var actor = Normalize(value) ?? "System";
        if (DeviceActivationCodeCodec.ContainsReservedActivationCode(actor))
        {
            return "[REDACTED]";
        }
        return actor[..Math.Min(128, actor.Length)];
    }

    private static string Redact(string value) =>
        DeviceActivationCodeCodec.RedactReservedActivationMetadata(value) ?? string.Empty;

    private static string? RedactNullable(string? value) =>
        DeviceActivationCodeCodec.RedactReservedActivationMetadata(value);

    private static ApiResponse<MobileDeviceActivationCodeCreateResponseDto> CreateError(
        string message,
        string errorCode) =>
        ApiResponse<MobileDeviceActivationCodeCreateResponseDto>.Error(message, errorCode);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private bool IsTrueAdministrator()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        return principal?.Identity?.IsAuthenticated == true
            && principal.Claims.Any(claim =>
                claim.Type == ClaimTypes.Role
                && Permissions.SuperAdminRoleNames.Any(role =>
                    role.Equals(claim.Value, StringComparison.OrdinalIgnoreCase)));
    }
}
