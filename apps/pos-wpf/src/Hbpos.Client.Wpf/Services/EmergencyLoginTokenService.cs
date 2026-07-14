using System.Globalization;
using BlazorApp.Shared.Security;
using Hbpos.Contracts.Cashiers;
using Microsoft.Extensions.Configuration;

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
    IConfiguration configuration,
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
            return CashierLoginResult.Fail("紧急登录二维码过长");
        }

        var now = _timeProvider.GetUtcNow();
        var trustedTime = await ReadTrustedTimeAsync(cancellationToken);
        if (trustedTime is not null && now < trustedTime.Value)
        {
            return CashierLoginResult.Fail("系统时间早于可信时间，请联网校时后重试");
        }

        var publicKeys = configuration.GetSection("EmergencyLogin:PublicKeys")
            .GetChildren()
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .ToDictionary(item => item.Key, item => item.Value!, StringComparer.Ordinal);
        if (!EmergencyLoginTokenCodec.TryVerify(
                token,
                publicKeys,
                now.UtcDateTime,
                out var payload,
                out var errorCode))
        {
            return CashierLoginResult.Fail(MapError(errorCode));
        }

        if (!string.Equals(payload!.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase))
        {
            return CashierLoginResult.Fail("紧急登录二维码不属于当前门店");
        }

        await SaveTrustedTimeAsync(now, cancellationToken);
        return CashierLoginResult.Success(CashierSessionContext.CreateEmergencyOverride(
            storeCode,
            deviceCode,
            payload.GrantId,
            new DateTimeOffset(payload.ExpiresAtUtc, TimeSpan.Zero),
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

    private static string MapError(string errorCode) => errorCode switch
    {
        "EMERGENCY_TOKEN_EXPIRED" => "紧急登录二维码已过期",
        "EMERGENCY_TOKEN_NOT_ACTIVE" => "紧急登录二维码尚未生效",
        "EMERGENCY_TOKEN_KEY_UNKNOWN" => "紧急登录签名密钥未知，请更新客户端",
        _ => "紧急登录二维码无效"
    };
}
