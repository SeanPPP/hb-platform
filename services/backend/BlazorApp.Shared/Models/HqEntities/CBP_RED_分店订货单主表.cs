using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities
{
    [SugarTable("CBP_RED_分店订货单主表")]
    public class CBP_RED_分店订货单主表Store
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int ID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? HGUID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 分店代码 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 订单号 { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? 订单日期 { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? 出库日期 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 运输费用 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 进口总金额 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 贴牌总金额 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 备注 { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? 流程状态 { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? 入库状态 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_Creator { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? FGC_CreateDate { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_LastModifier { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? FGC_LastModifyDate { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_Rowversion { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_UpdateHelp { get; set; }
    }
}
