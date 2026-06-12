using BlazorApp.Shared.Helper;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 角色权限关联表
    /// </summary>
    [SugarTable("HBwebSysRolePermissions")]
    public class SysRolePermission : BaseEntity
    {
        /// <summary>
        /// 关联ID (UUID v7)
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string Id { get; set; } = UuidHelper.GenerateUuid7();

        /// <summary>
        /// 角色GUID
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 50)]
        public string RoleGuid { get; set; } = string.Empty;

        /// <summary>
        /// 权限代码 (关联 SysPermission.Code)
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 100)]
        public string PermissionCode { get; set; } = string.Empty;
    }
}
