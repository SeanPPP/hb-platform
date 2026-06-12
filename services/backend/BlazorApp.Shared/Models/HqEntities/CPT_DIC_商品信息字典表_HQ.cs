using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities
{
    [SugarTable("CPT_DIC_商品信息字典表")]
    public class CPT_DIC_商品信息字典表
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int ID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? HGUID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 商品分类码GUID { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 供应商编码 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 商品编码 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 工厂货号 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? HB货号 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 条形码 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 英文名称 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 中文名称 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 中文拼音 { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? 商品类型 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 套装数量 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 材质 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 规格 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 单位 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 国内价格 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 进口价格 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 贴牌价格 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 商品图片 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? 腾讯云图地址 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 中包数量 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? 单件体积 { get; set; }

        [SugarColumn(IsNullable = true, ColumnName = "单件装箱数")]
        public decimal? 单件装箱数 { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? 使用状态 { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_Creator { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime FGC_CreateDate { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_LastModifier { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime FGC_LastModifyDate { get; set; }

        [SugarColumn(IsNullable = true, IsIgnore = true)]
        public string? FGC_Rowversion { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_UpdateHelp { get; set; }

        /// <summary>
        /// 导航属性 - 一对一关联到CBP_DIC_商品库存表
        /// 通过 商品编码 关联到 H商品编码
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        [Navigate(NavigateType.OneToOne, nameof(商品编码), nameof(CBP_DIC_商品库存表.H商品编码))]
        public CBP_DIC_商品库存表? 库存信息 { get; set; }
    }
}
