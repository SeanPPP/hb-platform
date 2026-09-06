using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb;

/// <summary>
/// 由管理员显式纳管的 RustDesk 设备。不得在此保存远程访问密码。
/// </summary>
[SugarTable("HBweb_RustDeskManagedDevice")]
public sealed class RustDeskManagedDevice
{
    [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
    public Guid Id { get; set; }

    [SugarColumn(Length = 100, IsNullable = false)]
    public string RustdeskId { get; set; } = string.Empty;

    [SugarColumn(Length = 120, IsNullable = false)]
    public string Alias { get; set; } = string.Empty;

    [SugarColumn(Length = 120, IsNullable = false)]
    public string Hostname { get; set; } = string.Empty;

    [SugarColumn(Length = 20, IsNullable = false)]
    public string Platform { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false)]
    public DateTime CreatedAtUtc { get; set; }

    [SugarColumn(IsNullable = false)]
    public bool IsDisabled { get; set; }
}
