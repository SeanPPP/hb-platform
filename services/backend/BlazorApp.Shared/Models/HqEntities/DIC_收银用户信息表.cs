using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities;

[SugarTable("DIC_收银用户信息表")]
public class DIC_收银用户信息表
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int ID { get; set; }

    [SugarColumn(ColumnName = "HGUID")]
    public string? HGUID { get; set; }

    [SugarColumn(ColumnName = "分店代码")]
    public string? 分店代码 { get; set; }

    [SugarColumn(ColumnName = "操作用户")]
    public string? 操作用户 { get; set; }

    [SugarColumn(ColumnName = "用户条码")]
    public string? 用户条码 { get; set; }

    [SugarColumn(ColumnName = "登陆角色")]
    public string? 登陆角色 { get; set; }

    [SugarColumn(ColumnName = "备注")]
    public string? 备注 { get; set; }

    [SugarColumn(ColumnName = "条码打印次数")]
    public int 条码打印次数 { get; set; }

    [SugarColumn(ColumnName = "状态")]
    public bool 状态 { get; set; }

    [SugarColumn(ColumnName = "FGC_Creator")]
    public string? FGC_Creator { get; set; }

    [SugarColumn(ColumnName = "FGC_CreateDate")]
    public DateTime FGC_CreateDate { get; set; }

    [SugarColumn(ColumnName = "FGC_LastModifier")]
    public string? FGC_LastModifier { get; set; }

    [SugarColumn(ColumnName = "FGC_LastModifyDate")]
    public DateTime FGC_LastModifyDate { get; set; }
}
