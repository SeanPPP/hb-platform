using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities;

[SugarTable("DIC_商品分类码表")]
public class DIC_商品分类码表
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int ID { get; set; }

    [SugarColumn(IsNullable = true)]
    public string? HGUID { get; set; }

    [SugarColumn(IsNullable = true)]
    public string? H类别名称 { get; set; }

    [SugarColumn(IsNullable = true)]
    public string? H父级GUID { get; set; }

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

    [SugarColumn(IsNullable = true)]
    public byte[]? FGC_Rowversion { get; set; }
}
