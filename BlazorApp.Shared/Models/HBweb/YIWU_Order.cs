using SqlSugar;
using BlazorApp.Shared.Models;

namespace BlazorApp.Shared.Models.HBweb
{
    /// <summary>
    /// 义乌订单表
    /// </summary>
    [SugarTable("YIWU_Order")]
    public class YIWU_Order : BaseEntity
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int ID { get; set; }

        /// <summary>
        /// 订单编号 格式: ORD-YYMMDD-01
        /// </summary>
        [SugarColumn(Length = 50)]
        public string? OrderNo { get; set; }

        ///导航属性
        //订单明细表
        [Navigate(NavigateType.OneToMany, nameof(YIWU_OrderDetail.OrderNo), nameof(OrderNo))]
        public List<YIWU_OrderDetail>? OrderDetails { get; set; }

        /// <summary>
        /// 供应商编码
        /// </summary>
        [SugarColumn(Length = 50)]
        public string? SupplierCode { get; set; }

        //导航属性国内供应商
        [Navigate(NavigateType.ManyToOne, nameof(SupplierCode), nameof(ChinaSupplier.SupplierCode))]
        public ChinaSupplier? ChinaSupplier { get; set; }

        /// <summary>
        /// 订单总金额  = 订单明细表.单价 * 订单明细表.数量
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? TotalAmount { get; set; }

        /// <summary>
        /// 订单总体积
        /// </summary>
        [SugarColumn(IsNullable = true)]//箱数*单件体积
        public decimal? TotalVolume { get; set; }

        /// <summary>
        /// 订单状态 0:草稿 1:已确认 2:已取消
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? OrderStatus { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? Remarks { get; set; }
    }
} 