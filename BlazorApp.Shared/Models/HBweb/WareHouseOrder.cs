using System;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 分店订货单主表（仓库订单）
    /// 功能：记录分店的订货单据头信息（订单号、日期、费用、总金额与流程状态等）
    /// 来源：HQ表 CBP_RED_分店订货单主表 同步落库
    /// </summary>
    [SugarTable("WareHouseOrder")]
    public class WareHouseOrder : BaseEntity
    {
        /// <summary>
        /// 订单GUID（主键）
        /// 映射：HQ主表 HGUID；本地新增时自动生成
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string OrderGUID { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 分店代码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? StoreCode { get; set; }

        /// <summary>
        /// 订单号
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? OrderNo { get; set; }

        /// <summary>
        /// 订单日期
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? OrderDate { get; set; }

        /// <summary>
        /// 出库日期
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? OutboundDate { get; set; }

        /// <summary>
        /// 运输费用
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 2)]
        public decimal? ShippingFee { get; set; }

        /// <summary>
        /// 进口总金额
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 2)]
        public decimal? ImportTotalAmount { get; set; }

        /// <summary>
        /// 贴牌总金额
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 2)]
        public decimal? OEMTotalAmount { get; set; }

        /// <summary>
        /// 备注（最长1000字符）
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 1000)]
        public string? Remarks { get; set; }

        /// <summary>
        /// 流程状态（如：草稿/审核中/已完成）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? FlowStatus { get; set; }

        /// <summary>
        /// 入库状态（如：未入库/部分入库/已入库）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? InboundStatus { get; set; }
    }
}
