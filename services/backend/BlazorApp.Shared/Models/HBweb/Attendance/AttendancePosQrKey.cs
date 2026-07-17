using SqlSugar;

namespace BlazorApp.Shared.Models;

[SugarTable("AttendancePosQrKey")]
public sealed class AttendancePosQrKey
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)]
    public string Kid { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 20)]
    public string Algorithm { get; set; } = "A256GCM";

    [SugarColumn(IsNullable = false, Length = 4096)]
    public string ProtectedKey { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 50)]
    public string StoreCode { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 50)]
    public string DeviceCode { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 100)]
    public string HardwareId { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 20)]
    public string Status { get; set; } = "Active";

    [SugarColumn(IsNullable = false)]
    public DateTime RegisteredAtUtc { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? RevokedAtUtc { get; set; }
}
