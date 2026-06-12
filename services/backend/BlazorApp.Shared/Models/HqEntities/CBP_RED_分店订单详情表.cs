using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities
{
    [SugarTable("CBP_RED_分店订单详情表")]
    public class CBP_RED_分店订单详情表Store
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int ID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? HGUID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 主表GUID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 分店代码 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 分店商品编码 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 商品编码 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 数量 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 配货数量 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 上次成本 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 进口价格 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 合计进口金额 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 贴牌价格 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 合计贴牌金额 { get; set; }

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
