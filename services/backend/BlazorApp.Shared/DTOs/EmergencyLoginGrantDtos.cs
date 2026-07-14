namespace BlazorApp.Shared.DTOs;

public sealed class EmergencyLoginGrantCreateRequestDto
{
    public string StoreCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class EmergencyLoginGrantRevokeRequestDto
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class EmergencyLoginGrantDto
{
    public Guid GrantId { get; set; }
    public string StoreCode { get; set; } = string.Empty;
    public DateOnly BusinessDate { get; set; }
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
    public string Status { get; set; } = string.Empty;
}

public sealed class EmergencyLoginGrantCreateResponseDto
{
    public EmergencyLoginGrantDto Grant { get; set; } = new();
    public string Token { get; set; } = string.Empty;
}
