using BlazorApp.Shared.Security;
using Hbpos.Api.Data;
using Microsoft.Extensions.Configuration;
using SqlSugar;

namespace Hbpos.Api.Auth;

public interface IEmergencyGrantAuthorizationService
{
    Task<EmergencyLoginTokenPayload?> ValidateAsync(
        string? token,
        string deviceStoreCode,
        CancellationToken cancellationToken);
}

public sealed class EmergencyGrantAuthorizationService(
    HbposSqlSugarContext dbContext,
    IConfiguration configuration,
    ILogger<EmergencyGrantAuthorizationService> logger,
    TimeProvider? timeProvider = null) : IEmergencyGrantAuthorizationService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<EmergencyLoginTokenPayload?> ValidateAsync(
        string? token,
        string deviceStoreCode,
        CancellationToken cancellationToken)
    {
        var publicKeys = configuration.GetSection("EmergencyLogin:PublicKeys")
            .GetChildren()
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .ToDictionary(item => item.Key, item => item.Value!, StringComparer.Ordinal);
        if (!EmergencyLoginTokenCodec.TryVerify(
                token,
                publicKeys,
                _timeProvider.GetUtcNow().UtcDateTime,
                out var payload,
                out _) ||
            !string.Equals(payload!.StoreCode, deviceStoreCode, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var grant = await dbContext.PosmDb.Queryable<EmergencyLoginGrantStatus>()
                .FirstAsync(item => item.GrantId == payload.GrantId, cancellationToken);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            return grant is not null &&
                grant.RevokedAtUtc is null &&
                grant.ExpiresAtUtc > now &&
                string.Equals(grant.StoreCode, deviceStoreCode, StringComparison.OrdinalIgnoreCase)
                ? payload
                : null;
        }
        catch (Exception ex)
        {
            // 撤销状态不可读时必须失败关闭，不能仅凭离线签名放行在线敏感操作。
            logger.LogError(ex, "读取紧急登录授权状态失败，GrantId={GrantId}", payload.GrantId);
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
