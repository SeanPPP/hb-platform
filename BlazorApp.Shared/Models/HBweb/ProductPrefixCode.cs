using SqlSugar;
using System.ComponentModel.DataAnnotations;
using BlazorApp.Shared.Helper;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 商品货号前缀表，存储供应商的商品货号前缀信息
    /// </summary>
    [SugarTable("ProductPrefixCode")]
    public class ProductPrefixCode : BaseEntity
    {
        /// <summary>
        /// 前缀编码（主键，使用UUID7格式）
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string PrefixCode { get; set; } = UuidHelper.GenerateUuid7();

        /// <summary>
        /// 供应商编码（外键关联ChinaSupplier）
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 50)]
        [Required(ErrorMessage = "供应商编码不能为空")]
        [Display(Name = "供应商编码")]
        public string SupplierCode { get; set; } = string.Empty;

        /// <summary>
        /// 前缀代码名称（如：HB、YW、GZ等）
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 10)]
        [Required(ErrorMessage = "前缀代码不能为空")]
        [Display(Name = "前缀代码")]
        public string PrefixName { get; set; } = string.Empty;

        /// <summary>
        /// 前缀说明/描述
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 200)]
        [Display(Name = "前缀说明")]
        public string? PrefixDescription { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        [SugarColumn(IsNullable = false)]
        [Display(Name = "是否启用")]
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 排序顺序
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "排序顺序")]
        public int? SortOrder { get; set; }

        /// <summary>
        /// 导航属性 - 关联供应商
        /// </summary>
        [Navigate(NavigateType.OneToOne, nameof(SupplierCode), nameof(ChinaSupplier.SupplierCode))]
        public ChinaSupplier? Supplier { get; set; }

    }
}

