using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities;

[SugarTable("DIC_商品信息字典表")]
public class DIC_商品信息字典表
{
    [SugarColumn(IsPrimaryKey = true)]
    public int ID { get; set; }
    public string? HGUID { get; set; }
    public string? H商品标签GUID { get; set; }
    public string? H商品分类码GUID { get; set; }
    public string? H供货商编码 { get; set; }
    public string? H商品编码 { get; set; }
    public string? H货号 { get; set; }
    public string? H主条形码 { get; set; }
    public string? H商品名称 { get; set; }
    public int H商品类型 { get; set; }
    public string? H大写名称 { get; set; }
    public string? H规格 { get; set; }
    public string? H单位 { get; set; }
    public decimal H进货价 { get; set; }
    public decimal H零售价 { get; set; }
    public bool H是否自动定价 { get; set; }
    public string? H商品图片 { get; set; }
    public int 中包数量 { get; set; }
    public string? H腾讯云图地址 { get; set; }
    public bool H使用状态 { get; set; }
    public bool H是否特殊商品 { get; set; }
    public string? H进货单主表GUID { get; set; }
    public string? H进货单详情GUID { get; set; }
    public string? CBP商品中文名称 { get; set; }
    public string? CBP供应商编码 { get; set; }
    public string? CBP商品分类码GUID { get; set; }
    public string? FGC_Creator { get; set; }
    public DateTime FGC_CreateDate { get; set; }
    public string? FGC_LastModifier { get; set; }
    public DateTime FGC_LastModifyDate { get; set; }
    //数据库忽略
    [SugarColumn(IsIgnore = true)]
    public int FGC_Rowversion { get; set; }
    public string? FGC_UpdateHelp { get; set; }
}
