using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities;

[SugarTable("DIC_商品零售价表")]
public class DIC_商品零售价表
{
    [SugarColumn(IsPrimaryKey = true)]
    public int ID { get; set; }
    public string HGUID { get; set; } = string.Empty;
    public string H分店代码 { get; set; } = string.Empty;
    public string H商品编码 { get; set; } = string.Empty;
    public string H分店商品编码 { get; set; } = string.Empty;
    public string H供应商编码 { get; set; } = string.Empty;
    public string H分店供应商编码 { get; set; } = string.Empty;
    public decimal H进货价 { get; set; }
    public decimal H分店零售价 { get; set; }
    public decimal H库存 { get; set; }
    public decimal H库存金额 { get; set; }
    public decimal H库存预警数 { get; set; }
    public DateTime H商品缺货日期 { get; set; }
    public bool H是否缺货状态 { get; set; }
    public decimal H最小订货量 { get; set; }
    public decimal H最小订货量合计金额 { get; set; }
    public string H活动类型 { get; set; } = string.Empty;
    public string H满减活动代码 { get; set; } = string.Empty;
    public DateTime H活动开始日期 { get; set; }
    public DateTime H活动结束日期 { get; set; }
    public decimal H折扣率 { get; set; }
    public decimal H满减数量 { get; set; }
    public decimal H满减金额 { get; set; }
    public decimal H多码数量 { get; set; }
    public bool H使用状态 { get; set; }
    public bool H是否自动定价 { get; set; }
    public decimal H自动新价格 { get; set; }
    public decimal H盘点入库记录数 { get; set; }
    public bool H是否特殊商品 { get; set; }
    public decimal H动态销售数量 { get; set; }
    public decimal H动态销售额 { get; set; }
    public decimal H动态成本 { get; set; }
    public decimal H动态毛利 { get; set; }
    public decimal H动态毛利率 { get; set; }
    public decimal H动态销售占比 { get; set; }
    public string FGC_Creator { get; set; } = string.Empty;
    public DateTime FGC_CreateDate { get; set; }
    public string FGC_LastModifier { get; set; } = string.Empty;
    public DateTime FGC_LastModifyDate { get; set; }
}
