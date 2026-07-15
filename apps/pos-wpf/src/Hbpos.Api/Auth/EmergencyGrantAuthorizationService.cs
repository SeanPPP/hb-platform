using BlazorApp.Shared.Security;
using Hbpos.Api.Data;
using SqlSugar;

namespace Hbpos.Api.Auth;

public interface IEmergencyGrantAuthorizationService
{
    Task<EmergencyLoginVerifiedClaims?> ValidateAsync(
        string? token,
        string deviceStoreCode,
        CancellationToken cancellationToken);
}

public sealed class EmergencyGrantAuthorizationService(
    HbposSqlSugarContext dbContext,
    IEmergencyLoginPublicKeyProvider publicKeyProvider,
    ILogger<EmergencyGrantAuthorizationService> logger,
    TimeProvider? timeProvider = null) : IEmergencyGrantAuthorizationService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<EmergencyLoginVerifiedClaims?> ValidateAsync(
        string? token,
        string deviceStoreCode,
        CancellationToken cancellationToken)
    {
        EmergencyLoginVerifiedClaims? claims;
        try
        {
            var publicKeys = await publicKeyProvider.GetKeysAsync(false, cancellationToken);
            var verified = EmergencyLoginTokenCodec.TryVerify(
                token,
                publicKeys,
                deviceStoreCode,
                _timeProvider.GetUtcNow().UtcDateTime,
                out claims,
                out var errorCode);
            if (!verified && string.Equals(errorCode, "EMERGENCY_TOKEN_KEY_UNKNOWN", StringComparison.Ordinal))
            {
                // 关键逻辑：轮换窗口遇到未知 KID 时绕过短缓存强制刷新一次。
                publicKeys = await publicKeyProvider.GetKeysAsync(true, cancellationToken);
                verified = EmergencyLoginTokenCodec.TryVerify(
                    token,
                    publicKeys,
                    deviceStoreCode,
                    _timeProvider.GetUtcNow().UtcDateTime,
                    out claims,
                    out _);
            }

            if (!verified)
            {
                return null;
            }
        }
        catch (Exception ex)
        {
            // 公钥数据库不可读时失败关闭，不能回退到本地配置或旧私有来源。
            logger.LogError(ex, "读取紧急登录验证公钥失败");
            return null;
        }

        try
        {
            var grant = await dbContext.PosmDb.Queryable<EmergencyLoginGrantStatus>()
                .FirstAsync(item => item.GrantId == claims!.GrantId, cancellationToken);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            return grant is not null &&
                grant.RevokedAtUtc is null &&
                grant.ExpiresAtUtc > now &&
                string.Equals(grant.StoreCode, deviceStoreCode, StringComparison.OrdinalIgnoreCase)
                ? claims
                : null;
        }
        catch (Exception ex)
        {
            // 撤销状态不可读时必须失败关闭，不能仅凭离线签名放行在线敏感操作。
            logger.LogError(ex, "读取紧急登录授权状态失败，GrantId={GrantId}", claims!.GrantId);
            return null;
        }
    }

    [SugarTable("POSM_EmergencyLoginGrant")]
    private sealed class EmergencyLoginGrantStatus
    {
        [SugarColumn(IsPrimaryKey = true)]
        public Guid GrantId { get; set; }

        public string StoreCode { get; set; } = string.Empty;

        public DateTime ExpiresAtUtc { get; set; }

        public DateTime? RevokedAtUtc { get; set; }
    }
}
