using BlazorApp.Shared.Helper;
using SqlSugar;

namespace BlazorApp.Shared.Models;

/// <summary>
/// 用户在指定分店的 POS 权限覆盖；无记录时继承角色和用户直接权限。
/// </summary>
[SugarTable("HBwebSysUserStorePosPermissions")]
public sealed class SysUserStorePosPermission : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsNullable = false, Length = 50)]
    public string Id { get; set; } = UuidHelper.GenerateUuid7();

    [SugarColumn(IsNullable = false, Length = 50)]
    public string UserGuid { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 50)]
    public string StoreGuid { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 100)]
    public string PermissionCode { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false)]
    public bool IsGranted { get; set; }
}
