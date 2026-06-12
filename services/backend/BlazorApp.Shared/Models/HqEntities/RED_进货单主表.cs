using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities
{
    [SugarTable("RED_进货单主表")]
    public class RED_进货单主表Store
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int ID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? HGUID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? APPGUID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? PCGUID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H分店代码 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H供应商编码 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H随货同行单号 { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? H单据类型 { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? H订单日期 { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? H入库日期 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? H总金额 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? H收货总金额 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H单据图片 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H备注 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H导入模板 { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? H流程状态 { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? H入库状态 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_Creator { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? FGC_CreateDate { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_LastModifier { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? FGC_LastModifyDate { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_UpdateHelp { get; set; }
    }
}
