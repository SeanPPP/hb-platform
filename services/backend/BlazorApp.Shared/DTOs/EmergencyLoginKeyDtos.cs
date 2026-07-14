using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs;

public sealed class EmergencyLoginKeyGenerateRequestDto
{
    [Required]
    public long? ExpectedVersion { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class EmergencyLoginKeyActivateRequestDto
{
    [Required]
    public long? ExpectedVersion { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool Force { get; set; }
}

public sealed class EmergencyLoginKeyRetireRequestDto
{
    [Required]
    public long? ExpectedVersion { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class EmergencyLoginKeyDto
{
    public string KeyId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PublicKeyPem { get; set; } = string.Empty;
    public string PublicKeyFingerprint { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CreatedReason { get; set; } = string.Empty;
    public DateTime? ActivatedAtUtc { get; set; }
    public string? ActivatedBy { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
    public string? RetiredBy { get; set; }
}

public sealed class EmergencyLoginKeyCoverageDto
{
    public int TotalDevices { get; set; }
    public int AcknowledgedDevices { get; set; }
    public decimal Percentage => TotalDevices == 0
        ? 100m
        : Math.Round(AcknowledgedDevices * 100m / TotalDevices, 2);
}

public sealed class EmergencyLoginKeyMissingDeviceDto
{
    public int DeviceRegistrationId { get; set; }
    public string? StoreCode { get; set; }
    public string DeviceNumber { get; set; } = string.Empty;
    public string HardwareId { get; set; } = string.Empty;
    public DateTime? LastOnlineAtUtc { get; set; }
    public DateTime? LastSyncAtUtc { get; set; }
}

public sealed class EmergencyLoginKeyListDto
{
    public string? ActiveKeyId { get; set; }
    public string? CoverageKeyId { get; set; }
    public long Version { get; set; }
    public bool DataProtectionHealthy { get; set; }
    public string DataProtectionStatus { get; set; } = string.Empty;
    public List<EmergencyLoginKeyDto> Keys { get; set; } = [];
    public EmergencyLoginKeyCoverageDto Coverage { get; set; } = new();
    public List<EmergencyLoginKeyMissingDeviceDto> MissingDevices { get; set; } = [];
}

public sealed class EmergencyLoginKeyMutationDto
{
    public long Version { get; set; }
    public string? ActiveKeyId { get; set; }
    public EmergencyLoginKeyDto Key { get; set; } = new();
}
