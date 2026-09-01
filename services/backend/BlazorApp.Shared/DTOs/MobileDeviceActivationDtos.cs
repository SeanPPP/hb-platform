using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs;

public static class MobileDeviceActivationHeaders
{
    public const string RecoveryOnly = "X-HB-Mobile-Activation-Recovery-Only";
}

public static class MobileDeviceActivationReasonCodes
{
    public const string Activated = "MOBILE_DEVICE_ACTIVATED";
    public const string Rebound = "MOBILE_DEVICE_REBOUND";
    public const string ActivationRecovered = "MOBILE_DEVICE_ACTIVATION_RECOVERED";
    public const string RebindRecovered = "MOBILE_DEVICE_REBIND_RECOVERED";
    public const string NotAvailable = "MOBILE_ACTIVATION_CODE_NOT_AVAILABLE";
    public const string PlatformMismatch = "MOBILE_ACTIVATION_PLATFORM_MISMATCH";
    public const string StoreUnavailable = "MOBILE_ACTIVATION_STORE_UNAVAILABLE";
    public const string AccountUnavailable = "MOBILE_ACTIVATION_ACCOUNT_UNAVAILABLE";
    public const string DeviceConflict = "MOBILE_DEVICE_ALREADY_BOUND";
    public const string DeviceStateConflict = "MOBILE_DEVICE_STATE_CONFLICT";
    public const string CredentialInvalid = "MOBILE_DEVICE_CREDENTIAL_INVALID";
    public const string BindingUnavailable = "MOBILE_DEVICE_BINDING_UNAVAILABLE";
}

public sealed record MobileDeviceActivationCodeCreateRequestDto(
    [Required, StringLength(50)] string StoreCode,
    [Required, StringLength(20)] string DeviceSystem,
    [Required, StringLength(64)] string TargetUserGuid,
    int ValidForMinutes,
    [Required, StringLength(200)] string Reason);

public sealed record MobileDeviceActivationCodeRevokeRequestDto(
    [Required, StringLength(200)] string Reason);

public sealed record MobileDeviceActivationManageableStoreDto(
    string StoreCode,
    string StoreName);

public sealed record MobileDeviceActivationManageableAccountDto(
    string UserGuid,
    string Username,
    string? FullName);

public sealed record MobileDeviceActivationGrantDto(
    Guid GrantId,
    string StoreCode,
    string? StoreName,
    string DeviceSystem,
    string Status,
    string TargetUserGuid,
    string TargetUsername,
    string? TargetFullName,
    DateTime CreatedAtUtc,
    string CreatedBy,
    string Reason,
    DateTime ExpiresAtUtc,
    DateTime? RevokedAtUtc,
    string? RevokedBy,
    string? RevokeReason,
    DateTime? ConsumedAtUtc,
    string? ConsumedHardwareId,
    string? ConsumedDeviceCode,
    string? ConsumptionKind);

public sealed record MobileDeviceActivationCodeCreateResponseDto(
    MobileDeviceActivationGrantDto Grant,
    string ActivationCode);

public sealed record MobileDeviceActivationPreviewRequestDto(
    [Required, StringLength(128, MinimumLength = 1)] string ActivationCode,
    [Required, StringLength(20, MinimumLength = 1)] string DeviceSystem);

public sealed record MobileDeviceActivationPreviewResponseDto(
    bool IsAllowed,
    string? ReasonCode,
    string? StoreCode,
    string? StoreName,
    string? DeviceSystem,
    string? TargetUsername,
    string? TargetFullName,
    int? AssignedStoreCount,
    DateTime? ExpiresAtUtc,
    string Message);

public sealed record MobileDeviceActivationRedeemRequestDto(
    [Required, StringLength(128, MinimumLength = 1)] string ActivationCode,
    [Required, StringLength(100, MinimumLength = 1)] string HardwareId,
    [Required, StringLength(20, MinimumLength = 1)] string DeviceSystem,
    [Required, StringLength(64, MinimumLength = 64)] string CredentialVerifier,
    [StringLength(200)] string? DeviceName);

public sealed record MobileDeviceActivationRebindRequestDto(
    [Required, StringLength(128, MinimumLength = 1)] string ActivationCode,
    [Required, StringLength(64, MinimumLength = 64)] string CredentialVerifier,
    [StringLength(200)] string? DeviceName,
    [StringLength(100)] string? CurrentHardwareId = null,
    [StringLength(256, MinimumLength = 16)] string? CurrentCredential = null);

public sealed record MobileDeviceBindingDto(
    Guid BindingId,
    int DeviceRegistrationId,
    string DeviceCode,
    string StoreCode,
    string StoreName,
    string DeviceSystem,
    string TargetUserGuid,
    string TargetUsername,
    string? TargetFullName,
    DateTime BoundAtUtc);

public sealed record MobileDeviceActivationMutationResponseDto(
    bool IsAllowed,
    string? ReasonCode,
    string Message,
    MobileDeviceBindingDto? Binding);

public sealed record MobileDeviceSessionExchangeRequestDto(
    [Required, StringLength(100, MinimumLength = 1)] string HardwareId,
    [Required, StringLength(256, MinimumLength = 16)] string Credential);

public sealed record MobileDeviceSessionStoreDto(
    string StoreGuid,
    string StoreCode,
    string StoreName,
    bool IsPrimary);

public sealed record MobileDeviceSessionUserDto(
    string UserGuid,
    string Username,
    string? FullName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<MobileDeviceSessionStoreDto> Stores);

public sealed record MobileDeviceSessionExchangeResponseDto(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string TokenType,
    string SessionKind,
    MobileDeviceSessionUserDto User);

public sealed record MobileDeviceUnbindRequestDto(
    [StringLength(200)] string? Reason = null);

public sealed record MobileDeviceUnbindResponseDto(bool Unbound);
