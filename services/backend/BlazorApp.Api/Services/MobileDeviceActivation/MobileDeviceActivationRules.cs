using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.POSM;
using BlazorApp.Shared.Security;

namespace BlazorApp.Api.Services.MobileDeviceActivation;

public readonly record struct MobileDeviceActivationGateDecision(
    bool IsAllowed,
    bool IsRecovery,
    string DeviceSystem,
    string? ReasonCode);

public sealed record MobileDeviceRegistrationState(
    int DeviceRegistrationId,
    string HardwareId,
    string DeviceCode,
    string StoreCode,
    string DeviceSystem,
    string DeviceType,
    int DeviceStatus);

public sealed record MobileDeviceTargetAccountState(
    string UserGuid,
    bool IsActive,
    bool IsDeleted,
    bool HasBoundStoreAccess);

public sealed record MobileDeviceBindingCredentialGate(
    bool RequiresBoundCredential,
    MobileDeviceAccountBinding? ActiveBinding);

public static class MobileDeviceActivationRules
{
    public static MobileDeviceBindingCredentialGate SelectBindingCredentialGate(
        IReadOnlyCollection<MobileDeviceAccountBinding> bindingRecords)
    {
        if (bindingRecords.Count == 0)
        {
            return new MobileDeviceBindingCredentialGate(false, null);
        }

        // 任意新域绑定历史都会永久接管该硬件的凭据门禁；仅唯一 active 记录可继续校验。
        var activeBindings = bindingRecords
            .Where(binding => binding.RevokedAtUtc == null)
            .Take(2)
            .ToList();
        return new MobileDeviceBindingCredentialGate(
            true,
            activeBindings.Count == 1 ? activeBindings[0] : null);
    }

    public static MobileDeviceActivationGateDecision EvaluatePreview(
        MobileDeviceActivationGrant? grant,
        ReadOnlySpan<byte> secret,
        string requestedDeviceSystem,
        DateTime utcNow)
    {
        // 匿名预览先收敛未知、撤销、过期和已消费状态，不能借平台错误枚举真实开通码。
        if (!IsAvailable(grant, secret, utcNow))
        {
            return Denied(MobileDeviceActivationReasonCodes.NotAvailable);
        }

        if (!TryNormalizeDeviceSystem(requestedDeviceSystem, out var normalized)
            || !string.Equals(grant!.DeviceSystem, normalized, StringComparison.Ordinal))
        {
            return Denied(MobileDeviceActivationReasonCodes.PlatformMismatch);
        }

        return new MobileDeviceActivationGateDecision(true, false, normalized, null);
    }

    public static MobileDeviceActivationGateDecision EvaluateRedeem(
        MobileDeviceActivationGrant grant,
        string hardwareId,
        string deviceSystem,
        bool recoveryOnly,
        DateTime utcNow)
    {
        if (grant.ConsumedAtUtc != null)
        {
            var exactRecovery = recoveryOnly
                && string.Equals(grant.ConsumedHardwareId, hardwareId, StringComparison.Ordinal)
                && string.Equals(grant.ConsumedDeviceSystem, deviceSystem, StringComparison.Ordinal)
                && grant.ConsumedBindingId != null;
            return exactRecovery
                ? new MobileDeviceActivationGateDecision(true, true, deviceSystem, null)
                : Denied(MobileDeviceActivationReasonCodes.NotAvailable);
        }

        if (grant.RevokedAtUtc != null || grant.ExpiresAtUtc <= utcNow || recoveryOnly)
        {
            return Denied(MobileDeviceActivationReasonCodes.NotAvailable);
        }

        if (!TryNormalizeDeviceSystem(deviceSystem, out var normalized)
            || !string.Equals(grant.DeviceSystem, normalized, StringComparison.Ordinal))
        {
            return Denied(MobileDeviceActivationReasonCodes.PlatformMismatch);
        }

        return new MobileDeviceActivationGateDecision(true, false, normalized, null);
    }

    public static bool TryNormalizeDeviceSystem(string? value, out string normalized)
    {
        normalized = value?.Trim() switch
        {
            "Android" => "Android",
            "iOS" => "iOS",
            _ => string.Empty,
        };
        return normalized.Length > 0;
    }

    public static bool IsTokenBindingValid(
        MobileDeviceAccountBinding? binding,
        MobileDeviceBindingContext context,
        MobileDeviceRegistrationState? registration,
        MobileDeviceTargetAccountState? account) =>
        binding != null
        && binding.BindingId == context.BindingId
        && binding.Version == context.BindingVersion
        && binding.DeviceRegistrationId == context.DeviceRegistrationId
        && string.Equals(binding.HardwareId, context.HardwareId, StringComparison.Ordinal)
        && string.Equals(binding.TargetUserGuid, context.UserGuid, StringComparison.Ordinal)
        && IsBindingRegistrationValid(binding, registration)
        && IsTargetAccountValid(binding, account);

    public static bool IsBoundCredentialValid(
        MobileDeviceAccountBinding? binding,
        MobileDeviceRegistrationState? registration,
        MobileDeviceTargetAccountState? account,
        string? credential) =>
        binding != null
        && IsBindingRegistrationValid(binding, registration)
        && IsTargetAccountValid(binding, account)
        && MobileDeviceCredentialCodec.MatchesCredential(
            binding.CredentialVerifier,
            credential);

    public static bool IsRebindSourceCredentialValid(
        MobileDeviceAccountBinding? binding,
        MobileDeviceRegistrationState? registration,
        string? credential) =>
        binding != null
        && IsBindingRegistrationValid(binding, registration)
        && MobileDeviceCredentialCodec.MatchesCredential(
            binding.CredentialVerifier,
            credential);

    public static bool IsBindingRegistrationValid(
        MobileDeviceAccountBinding? binding,
        MobileDeviceRegistrationState? registration) =>
        binding != null
        && binding.RevokedAtUtc == null
        && registration != null
        && registration.DeviceRegistrationId == binding.DeviceRegistrationId
        && string.Equals(registration.HardwareId, binding.HardwareId, StringComparison.Ordinal)
        && string.Equals(registration.DeviceCode, binding.DeviceCode, StringComparison.Ordinal)
        && string.Equals(registration.StoreCode, binding.StoreCode, StringComparison.OrdinalIgnoreCase)
        && string.Equals(registration.DeviceSystem, binding.DeviceSystem, StringComparison.Ordinal)
        && string.Equals(registration.DeviceType, "Mobile", StringComparison.Ordinal)
        && registration.DeviceStatus == 1;

    private static bool IsTargetAccountValid(
        MobileDeviceAccountBinding binding,
        MobileDeviceTargetAccountState? account) =>
        account != null
        && string.Equals(account.UserGuid, binding.TargetUserGuid, StringComparison.Ordinal)
        && account.IsActive
        && !account.IsDeleted
        && account.HasBoundStoreAccess;

    private static bool IsAvailable(
        MobileDeviceActivationGrant? grant,
        ReadOnlySpan<byte> secret,
        DateTime utcNow) =>
        grant != null
        && DeviceActivationCodeCodec.Matches(grant.SecretHash, secret)
        && grant.RevokedAtUtc == null
        && grant.ConsumedAtUtc == null
        && grant.ExpiresAtUtc > utcNow;

    private static MobileDeviceActivationGateDecision Denied(string reasonCode) =>
        new(false, false, string.Empty, reasonCode);
}
