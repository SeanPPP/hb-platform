using SqlSugar;
using System.ComponentModel.DataAnnotations;
using BlazorApp.Shared.Helper;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 国内商品创建记录表
    /// 用于记录批量创建商品的历史记录，便于追踪和审计
    /// </summary>
    [SugarTable("DomesticProductCreationLog")]
    public class DomesticProductCreationLog : BaseEntity
    {
        /// <summary>
        /// 记录ID（主键，使用UUID7格式）
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string LogId { get; set; } = UuidHelper.GenerateUuid7();

        /// <summary>
        /// 创建的商品编码（关联DomesticProduct表）
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 50)]
        [Required(ErrorMessage = "商品编码不能为空")]
        [Display(Name = "商品编码")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 供应商编码
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 50)]
        [Required(ErrorMessage = "供应商编码不能为空")]
        [Display(Name = "供应商编码")]
        public string SupplierCode { get; set; } = string.Empty;

        /// <summary>
        /// 供应商名称
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 200)]
        [Display(Name = "供应商名称")]
        public string? SupplierName { get; set; }

        /// <summary>
        /// HB货号
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 50)]
        [Required(ErrorMessage = "HB货号不能为空")]
        [Display(Name = "HB货号")]
        public string HBProductNo { get; set; } = string.Empty;

        /// <summary>
        /// 条形码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "条形码")]
        public string? Barcode { get; set; }

        /// <summary>
        /// 商品名称
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 200)]
        [Display(Name = "商品名称")]
        public string? ProductName { get; set; }

        /// <summary>
        /// 使用的前缀代码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "前缀代码")]
        public string? PrefixCode { get; set; }

        /// <summary>
        /// 前缀名称
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 10)]
        [Display(Name = "前缀名称")]
        public string? PrefixName { get; set; }

        /// <summary>
        /// 创建方式（Batch=批量创建，Single=单个创建，Import=导入创建）
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 20)]
        [Display(Name = "创建方式")]
        public string CreationType { get; set; } = "Batch";

        /// <summary>
        /// 批次号（同一批次创建的商品使用相同的批次号）
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "批次号")]
        public string? BatchNumber { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 500)]
        [Display(Name = "备注")]
        public string? Remark { get; set; }

        /// <summary>
        /// 导航属性 - 关联商品
        /// </summary>
        [Navigate(NavigateType.OneToOne, nameof(ProductCode), nameof(DomesticProduct.ProductCode))]
        public DomesticProduct? Product { get; set; }

        /// <summary>
        /// 导航属性 - 关联供应商
        /// </summary>
        [Navigate(NavigateType.OneToOne, nameof(SupplierCode), nameof(ChinaSupplier.SupplierCode))]
        public ChinaSupplier? Supplier { get; set; }
    }
}

