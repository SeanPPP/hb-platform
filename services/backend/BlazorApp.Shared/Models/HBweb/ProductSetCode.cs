using System.ComponentModel.DataAnnotations;
using BlazorApp.Shared.Helper;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 产品套装多码表，存储产品套装的多种编码信息
    /// </summary>
    [SugarTable("ProductSetCode")]
    public class ProductSetCode : BaseEntity
    {
        /// <summary>
        /// 套装编码主键（使用UUID7格式）
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false, Length = 50)]
        public string SetCodeId { get; set; } = UuidHelper.GenerateUuid7();

        /// <summary>
        /// 产品编码（外键关联Product表）
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 50)]
        [Required(ErrorMessage = "产品编码不能为空")]
        [Display(Name = "产品编码")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 产品编码（外键关联StoreMultiCodeProduct表）
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 50)]
        [Required(ErrorMessage = "产品编码不能为空")]
        [Display(Name = "产品多码编码")]
        public string SetProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 套装货号
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 50)]
        [Required(ErrorMessage = "套装货号不能为空")]
        [Display(Name = "套装货号")]
        public string SetItemNumber { get; set; } = string.Empty;

        /// <summary>
        /// 套装条码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "套装条码")]
        public string? SetBarcode { get; set; }

        /// <summary>
        /// 套装采购价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "套装采购价格")]
        public decimal? SetPurchasePrice { get; set; }

        /// <summary>
        /// 套装零售价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "套装零售价格")]
        public decimal? SetRetailPrice { get; set; }

        /// <summary>
        /// 套装数量
        /// </summary>
        [SugarColumn(IsNullable = false)]
        [Required(ErrorMessage = "套装数量不能为空")]
        [Range(1, int.MaxValue, ErrorMessage = "套装数量必须大于0")]
        [Display(Name = "套装数量")]
        public int SetQuantity { get; set; } = 1;

        /// <summary>
        /// 套装类型（1:组合套装, 2:固定套装, 3:变量套装）
        /// </summary>
        [SugarColumn(IsNullable = false)]
        [Required(ErrorMessage = "套装类型不能为空")]
        [Range(1, 3, ErrorMessage = "套装类型必须在1-3之间")]
        [Display(Name = "套装类型")]
        public int SetType { get; set; } = 1;

        /// <summary>
        /// 是否启用套装
        /// </summary>
        [SugarColumn(IsNullable = false)]
        [Display(Name = "是否启用")]
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 导航属性 - 关联产品
        /// </summary>
        [Navigate(NavigateType.OneToOne, nameof(ProductCode), nameof(Product.ProductCode))]
        public Product? Product { get; set; }

        /// <summary>
        /// 导航属性 - 关联多码产品
        /// </summary>
        [Navigate(
            NavigateType.OneToMany,
            nameof(SetProductCode),
            nameof(StoreMultiCodeProduct.MultiCodeProductCode)
        )]
        public StoreMultiCodeProduct? MultiCodeProduct { get; set; }

        /// <summary>
        /// 计算属性 - 套装类型描述
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public string SetTypeDescription
        {
            get
            {
                return SetType switch
                {
                    1 => "组合套装",
                    2 => "多码套装",
                    3 => "变量套装",
                    _ => "未知类型",
                };
            }
        }

        /// <summary>
        /// 计算属性 - 状态描述
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public string StatusDescription
        {
            get
            {
                if (IsDeleted)
                    return "已删除";
                return IsActive ? "启用" : "禁用";
            }
        }

        /// <summary>
        /// 计算属性 - 单品平均价格
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public decimal? AverageUnitPrice
        {
            get
            {
                if (SetRetailPrice.HasValue && SetQuantity > 0)
                    return SetRetailPrice.Value / SetQuantity;
                return null;
            }
        }

        /// <summary>
        /// 验证套装数据的有效性
        /// </summary>
        /// <returns>验证结果</returns>
        public (bool IsValid, string ErrorMessage) ValidateSetData()
        {
            if (string.IsNullOrWhiteSpace(ProductCode))
                return (false, "产品编码不能为空");

            if (string.IsNullOrWhiteSpace(SetItemNumber))
                return (false, "套装货号不能为空");

            if (SetQuantity <= 0)
                return (false, "套装数量必须大于0");

            if (SetType < 1 || SetType > 3)
                return (false, "套装类型必须在1-3之间");

            if (SetPurchasePrice.HasValue && SetPurchasePrice.Value < 0)
                return (false, "套装采购价格不能为负数");

            if (SetRetailPrice.HasValue && SetRetailPrice.Value < 0)
                return (false, "套装零售价格不能为负数");

            return (true, string.Empty);
        }
    }
}
