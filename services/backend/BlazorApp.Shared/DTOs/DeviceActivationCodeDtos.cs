namespace BlazorApp.Shared.DTOs;

public sealed record DeviceActivationCodeCreateRequestDto(
    string StoreCode,
    string DeviceSystem,
    int ValidForMinutes,
    string Reason);

public sealed record DeviceActivationCodeRevokeRequestDto(string Reason);

public sealed record DeviceActivationCodeManageableStoreDto(string StoreCode, string StoreName);

public sealed record DeviceActivationCodeGrantDto(
    Guid GrantId,
    string StoreCode,
    string? StoreName,
    string DeviceSystem,
    string Status,
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
    string? ConsumptionKind,
    string? PreviousStoreCode,
    string? PreviousDeviceCode);

public sealed record DeviceActivationCodeCreateResponseDto(
    DeviceActivationCodeGrantDto Grant,
    string ActivationCode);
