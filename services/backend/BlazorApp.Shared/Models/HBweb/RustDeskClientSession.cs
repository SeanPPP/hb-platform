using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb;

/// <summary>
/// RustDesk 客户端专用管理员会话。数据库只保存 opaque token 的哈希值。
/// </summary>
[SugarTable("HBweb_RustDeskClientSession")]
public sealed class RustDeskClientSession
{
    [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
    public Guid Id { get; set; }

    [SugarColumn(Length = 100, IsNullable = false)]
    public string UserGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 64, IsNullable = false)]
    public string TokenHash { get; set; } = string.Empty;

    [SugarColumn(Length = 64, IsNullable = false)]
    public string PasswordFingerprint { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false)]
    public DateTime CreatedAtUtc { get; set; }

    [SugarColumn(IsNullable = false)]
    public DateTime ExpiresAtUtc { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? RevokedAtUtc { get; set; }
}
