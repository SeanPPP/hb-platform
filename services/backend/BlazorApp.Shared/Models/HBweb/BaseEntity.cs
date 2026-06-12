using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 基础实体类，包含所有实体共有的字段
    /// </summary>
    public abstract class BaseEntity
    {
        /// <summary>
        /// 创建时间
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 创建者
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
        /// <summary>
        /// 更新者
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? UpdatedBy { get; set; }
        
        /// <summary>
        /// 是否已删除（软删除标记）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public bool IsDeleted { get; set; } = false;
    }
}