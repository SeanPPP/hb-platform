using SqlSugar;
using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.Models.HqEntities
{
    /// <summary>
    /// HQ分店实体表 指定数据库连接为HOT_HQ_CLOUD
    /// </summary>
    [SugarTable("DIC_分店信息表")]
    public class HqBranch
    {
        /// <summary>
        /// 分店代码
        /// </summary>
        /// //实际字段分店代码
        [SugarColumn(ColumnName = "H分店代码", IsNullable = false, Length = 50)]
        [Required(ErrorMessage = "分店代码不能为空")]
        [Display(Name = "分店代码")]
        public string BranchCode { get; set; } = string.Empty;

        /// <summary>
        /// 分店名称
        /// </summary>
        [SugarColumn(ColumnName = "H分店名称", IsNullable = false, Length = 200)]
        [Required(ErrorMessage = "分店名称不能为空")]
        [Display(Name = "分店名称")]
        public string BranchName { get; set; } = string.Empty;

        /// <summary>
        /// 商业编号
        /// </summary>
        [SugarColumn(ColumnName = "H商业编号", IsNullable = true, Length = 100)]
        [Display(Name = "商业编号")]
        public string? BusinessNumber { get; set; }

        /// <summary>
        /// 分店电话
        /// </summary>
        [SugarColumn(ColumnName = "H电话", IsNullable = true, Length = 200)]
        [Display(Name = "分店电话")]
        public string? Phone { get; set; }

        /// <summary>
        /// 店经理姓名
        /// </summary>
        [SugarColumn(ColumnName = "H店经理", IsNullable = true, Length = 100)]
        [Display(Name = "店经理")]
        public string? ManagerName { get; set; }

        /// <summary>
        /// 分店地址
        /// </summary>
        [SugarColumn(ColumnName = "H分店地址", IsNullable = true, Length = 500)]
        [Display(Name = "分店地址")]
        public string? Address { get; set; }


    }
}