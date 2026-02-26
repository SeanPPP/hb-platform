using System.ComponentModel.DataAnnotations;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 仓库商品信息表
    /// </summary>
    [SugarTable("WarehouseProduct")]
    public class WarehouseProduct : BaseEntity
    {
        /// <summary>
        /// 商品GUID商品编码
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string ProductCode { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 国内价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "国内价格")]
        public decimal? DomesticPrice { get; set; }

        /// <summary>
        /// 贴牌价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "贴牌价格")]
        public decimal? OEMPrice { get; set; }

        /// <summary>
        /// 进口价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "进口价格")]
        public decimal? ImportPrice { get; set; }

        /// <summary>
        /// 库存数量
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "库存")]
        public int? StockQuantity { get; set; }

        /// <summary>
        /// 最小订货量
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "最小订货量")]
        public int? MinOrderQuantity { get; set; }

        /// <summary>
        /// 库存金额
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "库存金额")]
        public decimal? StockValue { get; set; }

        /// <summary>
        /// 库存预警数
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "库存预警数")]
        public int? StockAlertQuantity { get; set; }

        /// <summary>
        /// 使用状态
        /// </summary>
        [SugarColumn(IsNullable = false)]
        [Display(Name = "使用状态")]
        public bool IsActive { get; set; } = true; //0-下架，1-上架

        /// <summary>
        /// 单件体积
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "单件体积")]
        public decimal? Volume { get; set; }

        /// <summary>
        /// 行版本号（乐观锁，用于并发控制）
        /// SqlSugar自动处理，每次更新时自动递增
        /// </summary>
        [SugarColumn(IsEnableUpdateVersionValidation = true, IsNullable = true)]
        [Display(Name = "版本号")]
        public byte[]? RowVersion { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(ProductCode))]
        public Product? Product { get; set; }

        //多对多
        //[Navigate(typeof(UserRole), nameof(UserRole.UserGUID), nameof(UserRole.RoleGUID))]
        [Navigate(
            typeof(ProductLocation),
            nameof(ProductLocation.ProductCode),
            nameof(ProductLocation.LocationGuid)
        )]
        public List<Location>? Locations { get; set; }

        //通过DomesticProduct表获取供应商名称
        [Navigate(NavigateType.OneToOne, nameof(ProductCode), nameof(DomesticProduct.ProductCode))]
        public DomesticProduct? DomesticProduct { get; set; }
    }
}
