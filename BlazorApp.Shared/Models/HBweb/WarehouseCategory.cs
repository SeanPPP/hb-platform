using SqlSugar;
using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 仓库分类表
    /// </summary>
    [SugarTable("WarehouseCategory")]
    public class WarehouseCategory : BaseEntity
    {
        /// <summary>
        /// 分类GUID
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string CategoryGUID { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 父级GUID
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? ParentGUID { get; set; }

        /// <summary>
        /// 类别名称
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 100)]
        [Required(ErrorMessage = "类别名称不能为空")]
        [Display(Name = "类别名称")]
        public string CategoryName { get; set; } = string.Empty;

        /// <summary>
        /// 中文名称
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 100)]
        [Display(Name = "中文名称")]
        public string? ChineseName { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 排序顺序
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? SortOrder { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 500)]
        public string? Remarks { get; set; }

        // 导航属性
        [Navigate(NavigateType.OneToMany, nameof(ParentGUID))]
        public List<WarehouseCategory> Children { get; set; } = new List<WarehouseCategory>();

        [Navigate(NavigateType.OneToOne, nameof(ParentGUID))]
        public WarehouseCategory? Parent { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(CategoryGUID))]
        public List<WarehouseProduct>? WarehouseProducts { get; set; }
    }
}