using SqlSugar;
using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 购物车项实体类，表示购物车中的具体商品项
    /// </summary>
    public class CartItem : BaseEntity
    {
        /// <summary>
        /// 购物车项全局唯一标识符
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string CartItemGUID { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 购物车GUID（外键）
        /// </summary>
        [SugarColumn(IsNullable = false)]
        [Required(ErrorMessage = "购物车GUID不能为空")]
        public string CartGUID { get; set; } = string.Empty;

        /// <summary>
        /// 商品GUID（外键）
        /// </summary>
        [SugarColumn(IsNullable = false)]
        [Required(ErrorMessage = "商品GUID不能为空")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 商品代码（冗余字段，用于快速查询）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? ItemNumber { get; set; }

        /// <summary>
        /// 商品名称（冗余字段，避免每次查询都关联产品表）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? ProductName { get; set; }

        /// <summary>
        /// 商品图片URL（冗余字段）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? ProductImage { get; set; }

        /// <summary>
        /// 单价（添加到购物车时的价格）
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 18, DecimalDigits = 4)]
        public decimal UnitPrice { get; set; }

        /// <summary>
        /// 数量
        /// </summary>
        [SugarColumn(IsNullable = false)]
        [Range(1, int.MaxValue, ErrorMessage = "数量必须大于0")]
        public int Quantity { get; set; } = 1;

        /// <summary>
        /// 实际配货价格（仓库管理员设置，也用于发票打印）
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 18, DecimalDigits = 4)]
        public decimal? ActualPrice { get; set; }

        /// <summary>
        /// 实际配货数量（仓库管理员设置，也是已分配数量）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? ActualQuantity { get; set; }

        /// <summary>
        /// 总价（计算字段）
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 18, DecimalDigits = 4)]
        public decimal? TotalPrice { get; set; }

        /// <summary>
        /// 商品体积（用于物流计算）
        /// </summary>
         [SugarColumn(IsNullable = true, Length = 18, DecimalDigits = 4)]
        public decimal? Volume { get; set; }

        /// <summary>
        /// 商品重量（用于物流计算）
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 18, DecimalDigits = 4)]
        public decimal? Weight { get; set; }

        /// <summary>
        /// 最小订货量（冗余字段）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? MinOrderQuantity { get; set; }

        /// <summary>
        /// 添加到购物车的时间
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public DateTime AddedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后更新时间
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public DateTime LastUpdated { get; set; } = DateTime.Now;

        /// <summary>
        /// 商品备注
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? Remarks { get; set; }

        /// <summary>
        /// 关联购物车（一对一导航属性）
        /// </summary>
        [Navigate(NavigateType.OneToOne, nameof(CartGUID))]
        public Cart? Cart { get; set; }

        /// <summary>
        /// 关联商品（一对一导航属性）
        /// </summary>
        [Navigate(NavigateType.OneToOne, nameof(ProductCode))]
        public WarehouseProduct? Product { get; set; }
    }
}