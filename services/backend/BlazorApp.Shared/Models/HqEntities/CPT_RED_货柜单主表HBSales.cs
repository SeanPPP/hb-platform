using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities
{
    [SugarTable("CPT_RED_货柜单主表")]
    public class CPT_RED_货柜单主表HBSales
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int ID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? HGUID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 货柜编号 { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? 装柜日期 { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? 预计到岸日期 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 合计件数 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 合计数量 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 合计金额 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 总体积 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 运费 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 备注 { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? 状态 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_Creator { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? FGC_CreateDate { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_LastModifier { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? FGC_LastModifyDate { get; set; }
    }
}
