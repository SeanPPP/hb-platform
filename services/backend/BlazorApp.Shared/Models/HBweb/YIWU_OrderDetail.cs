using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    /// <summary>
    /// 义乌订单明细表
    /// </summary>
    [SugarTable("YIWU_OrderDetail")]
    public class YIWU_OrderDetail : BaseEntity
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int ID { get; set; }

        /// <summary>
        /// 订单编号
        /// </summary>
        [SugarColumn(Length = 50)]
        public string? OrderNo { get; set; }

        //导航属性
        //订单主表
        [Navigate(NavigateType.ManyToOne, nameof(OrderNo), nameof(YIWU_Order.OrderNo))]
        public YIWU_Order? Order { get; set; }

        #region 商品信息
        /// <summary>
        /// 商品编码
        /// </summary>
        [SugarColumn(Length = 50)]
        public string? ProductCode { get; set; }

        /// <summary>
        /// HB货号
        /// </summary>
        [SugarColumn(Length = 50)]
        public string? HBProductNo { get; set; }

        /// <summary>
        /// 条形码
        /// </summary>
        [SugarColumn(Length = 50)]
        public string? Barcode { get; set; }

        /// <summary>
        /// 英文名称
        /// </summary>
        [SugarColumn(Length = 200)]
        public string? EnglishName { get; set; }

        /// <summary>
        /// 国内价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? DomesticPrice { get; set; }

        /// <summary>
        /// 零售价
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? OEMPrice { get; set; }

        /// <summary>
        /// 商品图片
        /// </summary>
        [SugarColumn(Length = 500)]
        public string? ProductImage { get; set; }

        /// <summary>
        /// 单件装箱数
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? PackingQuantity { get; set; }

        /// <summary>
        /// 单件体积
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? UnitVolume { get; set; }

        /// <summary>
        /// 中包数量
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? MiddlePackQuantity { get; set; }

        /// <summary>
        /// 使用状态
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? UsageStatus { get; set; }

        /// <summary>
        /// 供应商编码
        /// </summary>
        [SugarColumn(Length = 50)]
        public string? SupplierCode { get; set; }

        /// <summary>
        /// 供应商名称
        /// </summary>
        [SugarColumn(Length = 200)]
        public string? SupplierName { get; set; }
        #endregion

        #region 订货信息
        /// <summary>
        /// 订货总数量
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? OrderQuantity { get; set; }

        /// <summary>
        /// 订货箱数
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? OrderBoxes { get; set; }

        /// <summary>
        /// 订货金额 = 单价 * 数量
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? OrderAmount { get; set; }

        /// <summary>
        /// 订货体积
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? OrderVolume { get; set; }
        #endregion
    }
}
