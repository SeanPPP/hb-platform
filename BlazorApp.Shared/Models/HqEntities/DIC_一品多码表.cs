using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities;

/// <summary>
/// 一品多码表
/// </summary>
[SugarTable("DIC_一品多码表")]
public class DIC_一品多码表
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int ID { get; set; }

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? HGUID { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? H商品编码 { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? H多码商品编号 { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? H供应商编码 { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? H主条形码 { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? H多条形码 { get; set; } = string.Empty;

    [SugarColumn(DecimalDigits = 2, IsNullable = true)]
    public decimal? H进货价 { get; set; } = 0;

    [SugarColumn(DecimalDigits = 2, IsNullable = true)]
    public decimal? H一品多码零售价 { get; set; } = 0;

    [SugarColumn(IsNullable = true)]
    public bool? H使用状态 { get; set; } = false;

    [SugarColumn(IsNullable = true)]
    public bool? H是否自动定价 { get; set; } = false;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? FGC_Creator { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public DateTime? FGC_CreateDate { get; set; } = DateTime.Now;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? FGC_LastModifier { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public DateTime? FGC_LastModifyDate { get; set; } = DateTime.Now;

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? FGC_UpdateHelp { get; set; } = string.Empty;
}
