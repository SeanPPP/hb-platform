using System;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 分店本地供应商进货单主表
    /// 用途：记录分店对本地供应商的进货单据头信息（单据基本属性、金额与流程状态等）
    /// </summary>
    [SugarTable("StoreLocalSupplierInvoice")]
    public class StoreLocalSupplierInvoice : BaseEntity
    {
        /// <summary>
        /// 进货单主表GUID（主键）
        /// 来源：HQ主表 HGUID 映射；本地新增时自动生成
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string InvoiceGUID { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 应用侧GUID（APPGUID）
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? AppGUID { get; set; }

        /// <summary>
        /// 电脑侧GUID（PCGUID）
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? PcGUID { get; set; }

        /// <summary>
        /// 分店代码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? StoreCode { get; set; }

        /// <summary>
        /// 供应商编码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? SupplierCode { get; set; }

        /// <summary>
        /// 随货同行单号（物流/随货单据编号）
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 100)]
        public string? InvoiceNo { get; set; }

        /// <summary>
        /// 单据类型（枚举/字典）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? VoucherType { get; set; }

        /// <summary>
        /// 订单日期
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? OrderDate { get; set; }

        /// <summary>
        /// 入库日期
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? InboundDate { get; set; }

        /// <summary>
        /// 订单总金额
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? TotalAmount { get; set; }

        /// <summary>
        /// 收货总金额（到货后实收合计）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? ReceivedTotalAmount { get; set; }

        /// <summary>
        /// 单据图片（存储路径或URL）
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 1000)]
        public string? VoucherImage { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 1000)]
        public string? Remarks { get; set; }

        /// <summary>
        /// 导入模板名称/标识
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 100)]
        public string? ImportTemplate { get; set; }

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
