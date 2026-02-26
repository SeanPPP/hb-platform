using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities;

/// <summary>
/// 供应商信息表
/// </summary>
[SugarTable("DIC_供应商信息表")]
public class DIC_供应商信息表
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int ID { get; set; }

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? HGUID { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? H供应商编码 { get; set; } = string.Empty;

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? H供应商名称 { get; set; } = string.Empty;

    [SugarColumn(Length = 500, IsNullable = true)]
    public string? H供应商全称 { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public int? H商品导入模板 { get; set; }

    [SugarColumn(Length = 500, IsNullable = true)]
    public string? H公司地址 { get; set; } = string.Empty;

    [SugarColumn(Length = 100, IsNullable = true)]
    public string? H联系人 { get; set; } = string.Empty;

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? HEMAIL地址 { get; set; } = string.Empty;
    public DateTime FGC_CreateDate { get; set; }
    public DateTime FGC_LastModifyDate { get; set; }
}

