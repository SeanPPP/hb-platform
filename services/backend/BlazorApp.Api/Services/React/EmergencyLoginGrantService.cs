using System.Security.Cryptography;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.POSM;
using BlazorApp.Shared.Security;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

public sealed class EmergencyLoginSigningOptions
{
    public string ActiveKeyId { get; set; } = string.Empty;
    public Dictionary<string, string> PrivateKeys { get; set; } = new(StringComparer.Ordinal);
}

public sealed class EmergencyLoginGrantService
{
    private const int MaxReasonLength = 200;
    private const int MaxActorLength = 128;
    private readonly ISqlSugarClient _db;
    private readonly ICurrentUserManageableStoreScopeService _storeScopeService;
    private readonly EmergencyLoginSigningOptions _signingOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EmergencyLoginGrantService> _logger;

    public EmergencyLoginGrantService(
        POSMSqlSugarContext context,
        ICurrentUserManageableStoreScopeService storeScopeService,
        IOptions<EmergencyLoginSigningOptions> signingOptions,
        ILogger<EmergencyLoginGrantService> logger,
        TimeProvider? timeProvider = null
    )
    {
        _db = context.Db;
        _storeScopeService = storeScopeService;
        _signingOptions = signingOptions.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ApiResponse<List<EmergencyLoginGrantDto>>> ListAsync(string? storeCode)
    {
        var normalizedStore = NormalizeStoreCode(storeCode);
        if (normalizedStore == null)
        {
            return ApiResponse<List<EmergencyLoginGrantDto>>.Error(
                "门店代码不能为空",
                "EMERGENCY_GRANT_STORE_REQUIRED"
            );
        }

        if (!await _storeScopeService.CanAccessStoreCodeAsync(normalizedStore))
        {
            return ApiResponse<List<EmergencyLoginGrantDto>>.Error(
                "无权管理该门店的紧急登录授权",
                "EMERGENCY_GRANT_STORE_FORBIDDEN"
            );
        }

        var items = await _db
            .Queryable<EmergencyLoginGrantEntity>()
            .Where(item => item.StoreCode == normalizedStore)
            .OrderByDescending(item => item.IssuedAtUtc)
            .Take(50)
            .ToListAsync();
        var now = UtcNow();
        return ApiResponse<List<EmergencyLoginGrantDto>>.OK(
            items.Select(item => Map(item, now)).ToList()
        );
    }

    public async Task<ApiResponse<EmergencyLoginGrantCreateResponseDto>> CreateAsync(
        EmergencyLoginGrantCreateRequestDto request,
        string actor
    )
    {
        var storeCode = NormalizeStoreCode(request.StoreCode);
        var reason = NormalizeReason(request.Reason);
        if (storeCode == null)
        {
            return ApiResponse<EmergencyLoginGrantCreateResponseDto>.Error(
                "门店代码不能为空",
                "EMERGENCY_GRANT_STORE_REQUIRED"
            );
        }

        if (reason == null)
        {
            return ApiResponse<EmergencyLoginGrantCreateResponseDto>.Error(
                $"签发原因不能为空且不能超过 {MaxReasonLength} 个字符",
                "EMERGENCY_GRANT_REASON_INVALID"
            );
        }

        if (!await _storeScopeService.CanAccessStoreCodeAsync(storeCode))
        {
            return ApiResponse<EmergencyLoginGrantCreateResponseDto>.Error(
                "无权管理该门店的紧急登录授权",
                "EMERGENCY_GRANT_STORE_FORBIDDEN"
            );
        }

        var hasEnabledPos = await _db
            .Queryable<POSM_设备注册信息表>()
            .AnyAsync(device =>
                device.分店代码 == storeCode && device.设备状态 == 1 && device.设备类型 == "POS"
            );
        if (!hasEnabledPos)
        {
            return ApiResponse<EmergencyLoginGrantCreateResponseDto>.Error(
                "该门店没有已启用的 POS 设备",
                "EMERGENCY_GRANT_NO_ENABLED_POS"
            );
        }

        var now = UtcNow();
        var (businessDate, expiresAtUtc) = ResolveBusinessWindow(now);
        var existing = await FindActiveAsync(storeCode, businessDate);
        if (existing != null)
        {
            return ApiResponse<EmergencyLoginGrantCreateResponseDto>.Error(
                "该门店当天已有未撤销的紧急登录授权，请先撤销后重新生成",
                "EMERGENCY_GRANT_ALREADY_ACTIVE"
            );
        }

        var keyId = (_signingOptions.ActiveKeyId ?? string.Empty).Trim();
        if (
            string.IsNullOrWhiteSpace(keyId)
            || !_signingOptions.PrivateKeys.TryGetValue(keyId, out var privateKeyPem)
            || string.IsNullOrWhiteSpace(privateKeyPem)
        )
        {
            _logger.LogError("紧急登录签名密钥未配置或活动 KeyId 不存在，KeyId={KeyId}", keyId);
            return ApiResponse<EmergencyLoginGrantCreateResponseDto>.Error(
                "紧急登录签名密钥未配置",
                "EMERGENCY_GRANT_SIGNING_KEY_UNAVAILABLE"
            );
        }

        var normalizedActor = NormalizeActor(actor);
        var entity = new EmergencyLoginGrantEntity
        {
            GrantId = Guid.NewGuid(),
            StoreCode = storeCode,
            BusinessDate = businessDate.ToDateTime(TimeOnly.MinValue),
            KeyId = keyId,
            PermissionProfile = EmergencyLoginTokenCodec.AllPosTerminalProfile,
            IssuedBy = normalizedActor,
            IssuedReason = reason,
            IssuedAtUtc = now,
            NotBeforeUtc = now,
            ExpiresAtUtc = expiresAtUtc,
            UpdatedAtUtc = now,
        };
        string token;
        try
        {
            token = EmergencyLoginTokenCodec.Sign(
                new EmergencyLoginTokenPayload
                {
                    GrantId = entity.GrantId,
                    StoreCode = entity.StoreCode,
                    BusinessDate = businessDate.ToString("yyyy-MM-dd"),
                    Issuer = normalizedActor,
                    IssuedAtUtc = now,
                    NotBeforeUtc = now,
                    ExpiresAtUtc = expiresAtUtc,
                },
                keyId,
                privateKeyPem
            );
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            _logger.LogError(ex, "紧急登录令牌签名失败，KeyId={KeyId}", keyId);
            return ApiResponse<EmergencyLoginGrantCreateResponseDto>.Error(
                "紧急登录签名密钥无效",
                "EMERGENCY_GRANT_SIGNING_KEY_INVALID"
            );
        }

        try
        {
            // 关键逻辑：数据库只保存审计摘要，完整二维码令牌仅在本次响应返回一次。
            await _db.Insertable(entity).ExecuteCommandAsync();
        }
        catch (Exception ex)
        {
            if (await FindActiveAsync(storeCode, businessDate) != null)
            {
                return ApiResponse<EmergencyLoginGrantCreateResponseDto>.Error(
                    "该门店当天已有未撤销的紧急登录授权，请先撤销后重新生成",
                    "EMERGENCY_GRANT_ALREADY_ACTIVE"
                );
            }

            _logger.LogError(ex, "紧急登录授权摘要保存失败，StoreCode={StoreCode}", storeCode);
            throw;
        }

        _logger.LogWarning(
            "已签发门店紧急登录授权，GrantId={GrantId}, StoreCode={StoreCode}, IssuedBy={IssuedBy}, ExpiresAtUtc={ExpiresAtUtc}",
            entity.GrantId,
            storeCode,
            normalizedActor,
            expiresAtUtc
        );
        return ApiResponse<EmergencyLoginGrantCreateResponseDto>.OK(
            new EmergencyLoginGrantCreateResponseDto
            {
                Grant = Map(entity, now),
                Token = token,
            },
            "紧急登录二维码已生成；完整令牌仅显示本次"
        );
    }

    public async Task<ApiResponse<EmergencyLoginGrantDto>> RevokeAsync(
        Guid grantId,
        EmergencyLoginGrantRevokeRequestDto request,
        string actor
    )
    {
        var reason = NormalizeReason(request.Reason);
        if (reason == null)
        {
            return ApiResponse<EmergencyLoginGrantDto>.Error(
                $"撤销原因不能为空且不能超过 {MaxReasonLength} 个字符",
                "EMERGENCY_GRANT_REVOKE_REASON_INVALID"
            );
        }

        var entity = await _db
            .Queryable<EmergencyLoginGrantEntity>()
            .FirstAsync(item => item.GrantId == grantId);
        if (entity == null)
        {
            return ApiResponse<EmergencyLoginGrantDto>.Error(
                "紧急登录授权不存在",
                "EMERGENCY_GRANT_NOT_FOUND"
            );
        }

        if (!await _storeScopeService.CanAccessStoreCodeAsync(entity.StoreCode))
        {
            return ApiResponse<EmergencyLoginGrantDto>.Error(
                "无权管理该门店的紧急登录授权",
                "EMERGENCY_GRANT_STORE_FORBIDDEN"
            );
        }

        if (!entity.RevokedAtUtc.HasValue)
        {
            var now = UtcNow();
            var normalizedActor = NormalizeActor(actor);
            await _db
                .Updateable<EmergencyLoginGrantEntity>()
                .SetColumns(item => item.RevokedAtUtc == now)
                .SetColumns(item => item.RevokedBy == normalizedActor)
                .SetColumns(item => item.RevokedReason == reason)
                .SetColumns(item => item.UpdatedAtUtc == now)
                .Where(item => item.GrantId == grantId && item.RevokedAtUtc == null)
                .ExecuteCommandAsync();
            entity =
                await _db
                    .Queryable<EmergencyLoginGrantEntity>()
                    .FirstAsync(item => item.GrantId == grantId) ?? entity;
            _logger.LogWarning(
                "已撤销门店紧急登录授权，GrantId={GrantId}, StoreCode={StoreCode}, RevokedBy={RevokedBy}",
                grantId,
                entity.StoreCode,
                normalizedActor
            );
        }

        return ApiResponse<EmergencyLoginGrantDto>.OK(Map(entity, UtcNow()), "紧急登录授权已撤销");
    }

    private async Task<EmergencyLoginGrantEntity?> FindActiveAsync(
        string storeCode,
        DateOnly businessDate
    )
    {
        var date = businessDate.ToDateTime(TimeOnly.MinValue);
        return await _db
            .Queryable<EmergencyLoginGrantEntity>()
            .FirstAsync(item =>
                item.StoreCode == storeCode
                && item.BusinessDate == date
                && item.RevokedAtUtc == null
            );
    }

    internal static (DateOnly BusinessDate, DateTime ExpiresAtUtc) ResolveBusinessWindow(
        DateTime utcNow
    )
    {
        var zone = FindBrisbaneTimeZone();
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow.ToUniversalTime(), zone);
        var businessDate = DateOnly.FromDateTime(localNow);
        var nextMidnight = DateTime.SpecifyKind(localNow.Date.AddDays(1), DateTimeKind.Unspecified);
        return (businessDate, TimeZoneInfo.ConvertTimeToUtc(nextMidnight, zone));
    }

    private static TimeZoneInfo FindBrisbaneTimeZone()
    {
        foreach (var id in new[] { "Australia/Brisbane", "E. Australia Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // 尝试 Windows 与 IANA 两种时区标识，兼容部署平台。
            }
        }

        throw new TimeZoneNotFoundException("找不到 Australia/Brisbane 业务时区");
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static EmergencyLoginGrantDto Map(EmergencyLoginGrantEntity entity, DateTime now) =>
        new()
        {
            GrantId = entity.GrantId,
            StoreCode = entity.StoreCode,
            BusinessDate = DateOnly.FromDateTime(entity.BusinessDate),
            KeyId = entity.KeyId,
            PermissionProfile = entity.PermissionProfile,
            IssuedBy = entity.IssuedBy,
            IssuedReason = entity.IssuedReason,
            IssuedAtUtc = entity.IssuedAtUtc,
            NotBeforeUtc = entity.NotBeforeUtc,
            ExpiresAtUtc = entity.ExpiresAtUtc,
            RevokedAtUtc = entity.RevokedAtUtc,
            RevokedBy = entity.RevokedBy,
            RevokedReason = entity.RevokedReason,
            Status = entity.RevokedAtUtc.HasValue
                ? "Revoked"
                : entity.ExpiresAtUtc <= now
                    ? "Expired"
                    : "Active",
        };

    private static string? NormalizeStoreCode(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > 50 ? null : normalized;
    }

    private static string? NormalizeReason(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > MaxReasonLength
            ? null
            : normalized;
    }

    private static string NormalizeActor(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "System" : value.Trim();
        return normalized.Length <= MaxActorLength ? normalized : normalized[..MaxActorLength];
    }
}

[SugarTable("POSM_EmergencyLoginGrant")]
internal sealed class EmergencyLoginGrantEntity
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid GrantId { get; set; }
    public string StoreCode { get; set; } = string.Empty;
    public DateTime BusinessDate { get; set; }
    public string KeyId { get; set; } = string.Empty;
    public string PermissionProfile { get; set; } = string.Empty;
    public string IssuedBy { get; set; } = string.Empty;
    public string IssuedReason { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public DateTime NotBeforeUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedBy { get; set; }
    public string? RevokedReason { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
