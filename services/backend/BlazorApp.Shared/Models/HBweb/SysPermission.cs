using BlazorApp.Shared.Helper;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 系统权限定义表
    /// </summary>
    [SugarTable("HbwebSysPermissions")]
    public class SysPermission: BaseEntity
    {
        /// <summary>
        /// 权限ID (UUID v7)
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string Id { get; set; } = UuidHelper.GenerateUuid7();

        /// <summary>
        /// 权限代码 (业务唯一键, 如 User.Create)
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 100)]
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// 权限名称 (显示用)
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 权限分类 (如 用户管理)
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 50)]
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// 权限描述
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 200)]
        public string? Description { get; set; }
    }
}
