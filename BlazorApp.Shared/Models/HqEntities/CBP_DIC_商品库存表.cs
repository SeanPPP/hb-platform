using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities;

[SugarTable("CBP_DIC_商品库存表")]
public class CBP_DIC_商品库存表

{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int ID { get; set; }

    [SugarColumn(IsNullable = true)]
    public string? HGUID { get; set; }

    [SugarColumn(IsNullable = true)]
    public string? H商品编码 { get; set; }

    [SugarColumn(IsNullable = true)]
    public decimal? H国内价格 { get; set; }

    [SugarColumn(IsNullable = true)]
    public decimal? H贴牌价格 { get; set; }

    [SugarColumn(IsNullable = true)]
    public decimal? H进口价格 { get; set; }

    [SugarColumn(IsNullable = true)]
    public decimal? H库存 { get; set; }

    [SugarColumn(IsNullable = true)]
    public decimal? H最小订货量 { get; set; }

    [SugarColumn(IsNullable = true)]
    public decimal? H库存金额 { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? H库存预警数 { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? H使用状态 { get; set; }

    [SugarColumn(IsNullable = true)]
    public string? FGC_Creator { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? FGC_CreateDate { get; set; }

    [SugarColumn(IsNullable = true)]
    public string? FGC_LastModifier { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? FGC_LastModifyDate { get; set; }

    /// <summary>
    /// 导航属性 - 一对一关联到CPT_DIC_商品信息字典表
    /// 通过 H商品编码 关联到 商品编码
    /// </summary>
    [SugarColumn(IsIgnore = true)]
    [Navigate(NavigateType.OneToOne, nameof(H商品编码), nameof(CPT_DIC_商品信息字典表.商品编码))]
    public CPT_DIC_商品信息字典表? 商品信息 { get; set; }
}
