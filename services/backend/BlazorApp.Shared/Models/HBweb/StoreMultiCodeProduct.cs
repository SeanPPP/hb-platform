using System.ComponentModel.DataAnnotations;
using BlazorApp.Shared.Helper;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 分店一品多码表
    /// </summary>
    [SugarTable("StoreMultiCodeProduct")]
    public class StoreMultiCodeProduct : BaseEntity
    {
        /// <summary>
        /// 主键UUID
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false, Length = 50)]
        public string UUID { get; set; } = UuidHelper.GenerateUuid7();

        /// <summary>
        /// 分店代码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "分店代码")]
        public string? StoreCode { get; set; }

        /// <summary>
        /// 商品编码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "商品编码")]
        public string? ProductCode { get; set; }

        /// <summary>
        /// 多码商品编码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "多码商品编码")]
        public string? MultiCodeProductCode { get; set; }

        /// <summary>
        /// 分店多码商品编码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "分店多码商品编码")]
        public string? StoreMultiCodeProductCode { get; set; }

        /// <summary>
        /// 多条形码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "多条形码")]
        public string? MultiBarcode { get; set; }

        /// <summary>
        /// 进货价
        /// </summary>
        [SugarColumn(DecimalDigits = 2, IsNullable = true)]
        [Display(Name = "进货价")]
        public decimal? PurchasePrice { get; set; }

        /// <summary>
        /// 一品多码零售价
        /// </summary>
        [SugarColumn(DecimalDigits = 2, IsNullable = true)]
        [Display(Name = "一品多码零售价")]
        public decimal? MultiCodeRetailPrice { get; set; }

        /// <summary>
        /// 折扣率
        /// </summary>
        [SugarColumn(DecimalDigits = 4, IsNullable = true)]
        [Display(Name = "折扣率")]
        public decimal? DiscountRate { get; set; }

        /// <summary>
        /// 是否自动定价
        /// </summary>
        [SugarColumn(IsNullable = false)]
        [Display(Name = "是否自动定价")]
        public bool IsAutoPricing { get; set; } = false;

        /// <summary>
        /// 是否特殊商品
        /// </summary>
        [SugarColumn(IsNullable = false)]
        [Display(Name = "是否特殊商品")]
        public bool IsSpecialProduct { get; set; } = false;

        /// <summary>
        /// 使用状态
        /// </summary>
        [SugarColumn(IsNullable = false)]
        [Display(Name = "使用状态")]
        public bool IsActive { get; set; } = true;

        // 导航属性
        [Navigate(NavigateType.OneToOne, nameof(ProductCode))]
        public Product? Product { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(StoreCode))]
        public Store? Store { get; set; }
    }
}
