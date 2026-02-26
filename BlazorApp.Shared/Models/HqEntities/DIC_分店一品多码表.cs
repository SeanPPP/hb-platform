using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities;

/// <summary>
/// 分店一品多码表
/// </summary>
[SugarTable("DIC_分店一品多码表")]
public class DIC_分店一品多码表
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int ID { get; set; }

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? HGUID { get; set; } = string.Empty;

    [SugarColumn(Length = 20, IsNullable = true)]
    public string? H分店代码 { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? H商品编码 { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? H分店商品编码 { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? H多码商品编码 { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? H分店多码商品编码 { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? H供应商编码 { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? H主条形码 { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? H多条形码 { get; set; } = string.Empty;

    [SugarColumn(DecimalDigits = 2, IsNullable = true)]
    public decimal? H进货价 { get; set; } = 0;

    [SugarColumn(DecimalDigits = 4, IsNullable = true)]
    public decimal? H折扣率 { get; set; } = 0;

    [SugarColumn(DecimalDigits = 2, IsNullable = true)]
    public decimal? H一品多码零售价 { get; set; } = 0;

    [SugarColumn(DecimalDigits = 2, IsNullable = true)]
    public decimal? H库存 { get; set; } = 0;

    [SugarColumn(DecimalDigits = 2, IsNullable = true)]
    public decimal? H库存金额 { get; set; } = 0;

    [SugarColumn(DecimalDigits = 2, IsNullable = true)]
    public decimal? H自动新价格 { get; set; } = 0;

    [SugarColumn(DecimalDigits = 2, IsNullable = true)]
    public decimal? H库存预警数 { get; set; } = 0;

    [SugarColumn(IsNullable = true)]
    public DateTime? H商品缺货日期 { get; set; }

    [SugarColumn(IsNullable = true)]
    public bool? H是否缺货状态 { get; set; } = false;

    [SugarColumn(DecimalDigits = 2, IsNullable = true)]
    public decimal? H最小订货量 { get; set; } = 0;

    [SugarColumn(DecimalDigits = 2, IsNullable = true)]
    public decimal? H最小订货量合计金额 { get; set; } = 0;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? H活动类型 { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? H满减活动代码 { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public DateTime? H活动开始日期 { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? H活动结束日期 { get; set; }

    [SugarColumn(DecimalDigits = 2, IsNullable = true)]
    public decimal? H满减数量 { get; set; } = 0;

    [SugarColumn(DecimalDigits = 2, IsNullable = true)]
    public decimal? H满减金额 { get; set; } = 0;

    [SugarColumn(IsNullable = true)]
    public bool? H是否自动定价 { get; set; } = false;

    [SugarColumn(IsNullable = true)]
    public bool? H是否特殊商品 { get; set; } = false;

    [SugarColumn(Length = 20, IsNullable = true)]
    public string? H商品柜组号 { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public bool? H使用状态 { get; set; } = false;

    [SugarColumn(DecimalDigits = 2, IsNullable = true)]
    public decimal? H动态销售数量 { get; set; } = 0;

    [SugarColumn(DecimalDigits = 2, IsNullable = true)]
    public decimal? H动态销售额 { get; set; } = 0;

    [SugarColumn(DecimalDigits = 2, IsNullable = true)]
    public decimal? H动态成本 { get; set; } = 0;

    [SugarColumn(DecimalDigits = 2, IsNullable = true)]
    public decimal? H动态毛利 { get; set; } = 0;

    [SugarColumn(DecimalDigits = 4, IsNullable = true)]
    public decimal? H动态毛利率 { get; set; } = 0;

    [SugarColumn(DecimalDigits = 4, IsNullable = true)]
    public decimal? H动态销售占比 { get; set; } = 0;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? FGC_Creator { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public DateTime? FGC_CreateDate { get; set; } = DateTime.Now;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? FGC_LastModifier { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public DateTime? FGC_LastModifyDate { get; set; } = DateTime.Now;
}
