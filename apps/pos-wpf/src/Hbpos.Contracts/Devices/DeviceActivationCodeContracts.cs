using System.ComponentModel.DataAnnotations;

namespace Hbpos.Contracts.Devices;

public static class DeviceActivationHeaders
{
    public const string RecoveryOnly = "X-HBPOS-Activation-Recovery-Only";
}

public static class DeviceActivationReasonCodes
{
    public const string Activated = "ACTIVATED";
    public const string ActivationRecovered = "ACTIVATION_RECOVERED";
    public const string ActivationCodeRequired = "ACTIVATION_CODE_REQUIRED";
    public const string NotAvailable = "ACTIVATION_CODE_NOT_AVAILABLE";
    public const string PlatformMismatch = "ACTIVATION_PLATFORM_MISMATCH";
    public const string StoreUnavailable = "STORE_UNAVAILABLE";
    public const string DeviceConflict = "DEVICE_ALREADY_REGISTERED";
    public const string TargetStoreUnchanged = "TARGET_STORE_UNCHANGED";
    public const string DeviceStateConflict = "DEVICE_STATE_CONFLICT";
}

public sealed record DeviceActivationCodePreviewRequest(
    [Required(AllowEmptyStrings = false), StringLength(128, MinimumLength = 1)]
    string ActivationCode,
    [Required(AllowEmptyStrings = false), StringLength(20, MinimumLength = 1)]
    string DeviceSystem);

public sealed record DeviceActivationCodePreviewResponse(
    bool IsAllowed,
    string? ReasonCode,
    string? StoreCode,
    string? StoreName,
    string? DeviceSystem,
    DateTime? ExpiresAtUtc,
    string Message);

public sealed record DeviceActivationCodeRedeemRequest(
    [Required(AllowEmptyStrings = false), StringLength(128, MinimumLength = 1)]
    string ActivationCode,
    [Required(AllowEmptyStrings = false), StringLength(100, MinimumLength = 1)]
    string HardwareId,
    [StringLength(200)]
    string? TerminalName,
    [Required(AllowEmptyStrings = false), StringLength(20, MinimumLength = 1)]
    string DeviceSystem);

public sealed record DeviceActivationCodeRebindRequest(
    [Required(AllowEmptyStrings = false), StringLength(128, MinimumLength = 1)]
    string ActivationCode,
    [StringLength(200)]
    string? TerminalName);

public sealed record DeviceActivationCodeRedeemResponse(
    string DeviceCode,
    string StoreCode,
    string StoreName,
    int DeviceStatus,
    bool IsAllowed,
    string? Message = null,
    string? AuthorizationCode = null,
    string? ReasonCode = null);
