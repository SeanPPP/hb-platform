using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities
{
    [SugarTable("RED_进货单详情表")]
    public class RED_进货单详情表Store
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int ID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? HGUID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H主表GUID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H分店代码 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H商品标签GUID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H商品分类码GUID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H供应商编码 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H分店商品编码 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H商品编码 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H货号 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H主条形码 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H商品名称 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H规格 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H单位 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? H数量 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? H上次进货价 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? H进货价 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? H零售价 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? H合计金额 { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? H已存在商品数 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H商品图片 { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? H活动类型 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? H折扣率 { get; set; }

        [SugarColumn(IsNullable = true)]
        public bool? H是否自动定价 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? H定价浮率 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? H新自动零售价 { get; set; }

        [SugarColumn(IsNullable = true)]
        public bool? H是否特殊商品 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? H老库分店商品编码 { get; set; }

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
