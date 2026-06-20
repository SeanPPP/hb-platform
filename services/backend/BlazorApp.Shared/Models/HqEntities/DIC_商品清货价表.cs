using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities;

[SugarTable("DIC_商品清货价表")]
public class DIC_商品清货价表
{
    [SugarColumn(IsPrimaryKey = true)]
    public int ID { get; set; }
    public string HGUID { get; set; } = string.Empty;
    public string 分店代码 { get; set; } = string.Empty;
    public string 商品编码 { get; set; } = string.Empty;
    public string 清货条形码 { get; set; } = string.Empty;
    public decimal 清货价 { get; set; }
    public string FGC_Creator { get; set; } = string.Empty;
    public DateTime FGC_CreateDate { get; set; }
    public string FGC_LastModifier { get; set; } = string.Empty;
    public DateTime FGC_LastModifyDate { get; set; }
}
