using SqlSugar;
using System.ComponentModel.DataAnnotations;
using BlazorApp.Shared.Helper;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 分店商品清货价表
    /// </summary>
    [SugarTable("StoreClearancePrice")]
    public class StoreClearancePrice : BaseEntity
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
        /// 清货条形码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "清货条形码")]
        public string? ClearanceBarcode { get; set; }

        /// <summary>
        /// 清货价
        /// </summary>
        [SugarColumn(DecimalDigits = 2, IsNullable = true)]
        [Display(Name = "清货价")]
        public decimal? ClearancePrice { get; set; }

        // 导航属性
        [Navigate(NavigateType.OneToOne, nameof(ProductCode))]
        public Product? Product { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(StoreCode))]
        public Store? Store { get; set; }
    }
}
