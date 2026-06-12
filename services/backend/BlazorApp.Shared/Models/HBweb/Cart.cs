using SqlSugar;
using System.ComponentModel.DataAnnotations;
using BlazorApp.Shared.Constants;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 购物车实体类，表示用户的购物车信息
    /// </summary>
    public class Cart : BaseEntity
    {
        /// <summary>
        /// 购物车全局唯一标识符
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string CartGUID { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 用户GUID（外键）
        /// </summary>
        [SugarColumn(IsNullable = false)]
        [Required(ErrorMessage = "用户GUID不能为空")]
        public string UserGUID { get; set; } = string.Empty;

        /// <summary>
        /// 门店GUID（外键，可空 - 购物车创建时不绑定门店，订单确认时选择）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? StoreGUID { get; set; }

        /// <summary>
        /// 购物车名称
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? CartName { get; set; }

        /// <summary>
        /// 购物车订单号（格式：ORD-YYYY-0001）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? OrderNumber { get; set; }

        /// <summary>
        /// 购物车发票号（格式：INV-YYYY-0001）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? InvoiceNumber { get; set; }

        /// <summary>
        /// 购物车状态（Active/Save/Submitted）
        /// Active: 活跃购物车, Save: 已保存, Submitted: 已提交, OrderStatus: 等待/配货中/已发货/已到货
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public string CartStatus { get; set; } = CartStatusConstants.Active;

        /// <summary>
        /// 总金额（计算字段，可以从CartItems计算得出）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? TotalAmount { get; set; }

        /// <summary>
        /// 总商品数量（计算字段）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? TotalQuantity { get; set; }

        /// <summary>
        /// 总商品体积 
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? TotalVolume { get; set; }

        /// <summary>
        /// 最后修改时间
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public DateTime LastModified { get; set; } = DateTime.Now;

        /// <summary>
        /// 过期时间（用于清理长期未使用的购物车）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// 购物车备注信息
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 1000)]
        public string? Remarks { get; set; }

        /// <summary>
        /// 折扣金额
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? Discount { get; set; }

        /// <summary>
        /// 运费
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? FreightFee { get; set; }

        /// <summary>
        /// GST税费 (10%)
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? GST { get; set; }

        /// <summary>
        /// 购物车项列表（一对多导航属性）
        /// </summary>
        [Navigate(NavigateType.OneToMany, nameof(CartItem.CartGUID))]
        public List<CartItem>? CartItems { get; set; }

        /// <summary>
        /// 关联用户（一对一导航属性）
        /// </summary>
        [Navigate(NavigateType.OneToOne, nameof(UserGUID))]
        public User? User { get; set; }

        /// <summary>
        /// 关联门店（一对一导航属性）
        /// </summary>
        [Navigate(NavigateType.OneToOne, nameof(StoreGUID))]
        public Store? Store { get; set; }
    }
}