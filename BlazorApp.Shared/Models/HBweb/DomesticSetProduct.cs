using SqlSugar;
using System.ComponentModel.DataAnnotations;
using BlazorApp.Shared.Helper;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 套装商品信息表，存储套装商品的详细信息
    /// </summary>
    [SugarTable("DomesticSetProduct")]
    public class DomesticSetProduct : BaseEntity
    {
        /// <summary>
        /// 套装商品编码（主键，使用UUID7格式）
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string SetProductCode { get; set; } = UuidHelper.GenerateUuid7();

        /// <summary>
        /// 商品编码（外键关联DomesticProduct）
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 50)]
        [Required(ErrorMessage = "商品编码不能为空")]
        [Display(Name = "商品编码")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 商品货号
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "商品货号")]
        public string? ProductNo { get; set; }

        /// <summary>
        /// 套装货号
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 50)]
        [Required(ErrorMessage = "套装货号不能为空")]
        [Display(Name = "套装货号")]
        public string SetProductNo { get; set; } = string.Empty;

        /// <summary>
        /// 套装条码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "套装条码")]
        public string? SetBarcode { get; set; }

        /// <summary>
        /// 国内价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "国内价格")]
        public decimal? DomesticPrice { get; set; }

        /// <summary>
        /// 进口价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "进口价格")]
        public decimal? ImportPrice { get; set; }

        /// <summary>
        /// 贴牌价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "贴牌价格")]
        public decimal? OEMPrice { get; set; }

       
        /// <summary>
        /// 备注信息
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 1000)]
        [Display(Name = "备注")]
        public string? Remarks { get; set; }

        /// <summary>
        /// 导航属性 - 关联国内商品
        /// </summary>
        [Navigate(NavigateType.OneToOne, nameof(ProductCode), nameof(DomesticProduct.ProductCode))]
        public DomesticProduct? DomesticProduct { get; set; }

    }
}
