using System.ComponentModel.DataAnnotations;
using BlazorApp.Shared.Helper;
 using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 分店商品零售价表
    /// </summary>
    [SugarTable("StoreRetailPrice")]
    public class StoreRetailPrice : BaseEntity
    {
        /// <summary>
        /// 主键UUID 分店商品编码
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
        /// 分店商品编码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "分店商品编码")]
        public string? StoreProductCode { get; set; }

        /// <summary>
        /// 供应商编码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "供应商编码")]
        public string? SupplierCode { get; set; }

        /// <summary>
        /// 进货价
        /// </summary>
        [SugarColumn(DecimalDigits = 2, IsNullable = true)]
        [Display(Name = "进货价")]
        public decimal? PurchasePrice { get; set; }

        /// <summary>
        /// 分店零售价
        /// </summary>
        [SugarColumn(DecimalDigits = 2, IsNullable = true)]
        [Display(Name = "分店零售价")]
        public decimal? StoreRetailPriceValue { get; set; }

        /// <summary>
        /// 折扣率
        /// </summary>
        [SugarColumn(DecimalDigits = 4, IsNullable = true)]
        [Display(Name = "折扣率")]
        public decimal? DiscountRate { get; set; }

        /// <summary>
        /// 使用状态
        /// </summary>
        [SugarColumn(IsNullable = false)]
        [Display(Name = "使用状态")]
        public bool IsActive { get; set; } = true;

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

        // 导航属性
        [Navigate(NavigateType.OneToOne, nameof(ProductCode))]
        public Product? Product { get; set; }

        //供应商
        [Navigate(NavigateType.OneToOne, nameof(SupplierCode))]
        public HBLocalSupplier? Supplier { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(StoreCode))]
        public Store? Store { get; set; }
    }
}
