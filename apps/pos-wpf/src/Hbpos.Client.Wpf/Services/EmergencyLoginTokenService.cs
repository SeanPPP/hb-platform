using System.Globalization;
using BlazorApp.Shared.Security;
using Hbpos.Contracts.Cashiers;

namespace Hbpos.Client.Wpf.Services;

public interface IEmergencyLoginTokenService
{
    Task<CashierLoginResult> LoginAsync(
        string token,
        string storeCode,
        string deviceCode,
        CancellationToken cancellationToken = default);
}

public sealed class EmergencyLoginTokenService(
    IEmergencyLoginPublicKeyCache publicKeyCache,
    IEmergencyLoginPublicKeySyncService publicKeySyncService,
    ILocalAppSettingsRepository settingsRepository,
    IDeviceAuthorizationProtector protector,
    TimeProvider? timeProvider = null) : IEmergencyLoginTokenService
{
    private const string TrustedTimeKey = "emergency-login:trusted-time:v1";
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<CashierLoginResult> LoginAsync(
        string token,
        string storeCode,
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        if (token.Length > EmergencyLoginTokenCodec.MaxTokenLength)
        {
            return CashierLoginResult.Fail("紧急登录二维码过长", "EMERGENCY_TOKEN_TOO_LONG");
        }

        var now = _timeProvider.GetUtcNow();
        var trustedTime = await ReadTrustedTimeAsync(cancellationToken);
        if (trustedTime is not null && now < trustedTime.Value)
        {
            return CashierLoginResult.Fail("系统时间早于可信时间，请联网校时后重试", "EMERGENCY_CLOCK_ROLLBACK");
        }

        EmergencyLoginVerifiedClaims? claims;
        string errorCode;
        try
        {
            var publicKeys = await ReadPublicKeysAsync(cancellationToken);
            var verified = EmergencyLoginTokenCodec.TryVerify(
                token,
                publicKeys,
                storeCode,
                now.UtcDateTime,
                out claims,
                out errorCode);
            if (!verified && string.Equals(errorCode, "EMERGENCY_TOKEN_KEY_UNKNOWN", StringComparison.Ordinal))
            {
                // 关键逻辑：轮换期间只在未知 KID 时立即联网同步一次，再用新缓存重验。
                _ = await publicKeySyncService.SyncAsync(cancellationToken);
                publicKeys = await ReadPublicKeysAsync(cancellationToken);
                verified = EmergencyLoginTokenCodec.TryVerify(
                    token,
                    publicKeys,
                    storeCode,
                    now.UtcDateTime,
                    out claims,
                    out errorCode);
            }

            if (!verified)
            {
                return CashierLoginResult.Fail(MapError(errorCode), errorCode);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // 公钥缓存或同步不可用只影响紧急登录，不得抛出并干扰普通收银员登录链路。
            return CashierLoginResult.Fail(MapError("EMERGENCY_TOKEN_KEY_UNKNOWN"), "EMERGENCY_TOKEN_KEY_UNKNOWN");
        }

        await SaveTrustedTimeAsync(now, cancellationToken);
        return CashierLoginResult.Success(CashierSessionContext.CreateEmergencyOverride(
            storeCode,
            deviceCode,
            claims!.GrantId,
            new DateTimeOffset(claims.ExpiresAtUtc, TimeSpan.Zero),
            token));
    }

    private async Task<DateTimeOffset?> ReadTrustedTimeAsync(CancellationToken cancellationToken)
    {
        var value = protector.Unprotect(await settingsRepository.GetValueAsync(TrustedTimeKey, cancellationToken));
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private Task SaveTrustedTimeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var protectedValue = protector.Protect(now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            ?? throw new InvalidOperationException("无法保护紧急登录可信时间。");
        return settingsRepository.SetValueAsync(TrustedTimeKey, protectedValue, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadPublicKeysAsync(
        CancellationToken cancellationToken)
    {
        var package = await publicKeyCache.GetAsync(cancellationToken);
        if (package is null || !EmergencyLoginPublicKeyValidator.TryValidate(package))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return package.Keys.ToDictionary(key => key.Kid, key => key.PublicKeyPem, StringComparer.Ordinal);
    }

    private static string MapError(string errorCode) => errorCode switch
    {
        "EMERGENCY_TOKEN_EXPIRED" => "紧急登录二维码已过期",
        "EMERGENCY_TOKEN_NOT_ACTIVE" => "紧急登录二维码尚未生效",
        "EMERGENCY_TOKEN_KEY_UNKNOWN" => "紧急登录签名密钥未知，请更新客户端",
        "EMERGENCY_TOKEN_WRONG_STORE" => "紧急登录二维码不属于当前门店",
        _ => "紧急登录二维码无效"
    };
}
