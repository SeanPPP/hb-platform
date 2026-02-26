using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities
{
    /// <summary>
    /// 货柜单详情表
    /// </summary>
    [SugarTable("CPT_RED_货柜单详情表")]
    public class CPT_RED_货柜单详情表Store
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int ID { get; set; }

        /// <summary>
        /// 全局唯一标识
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? HGUID { get; set; }

        /// <summary>
        /// 主表GUID
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? 主表GUID { get; set; }

        /// <summary>
        /// 商品编码
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? 商品编码 { get; set; }

        //导航属性 到CPT_DIC_商品信息字典表
        [Navigate(NavigateType.OneToOne, nameof(商品编码), nameof(CPT_DIC_商品信息字典表.商品编码))]
        public CPT_DIC_商品信息字典表? 商品信息 { get; set; }

        /// <summary>
        /// 装柜类型
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? 装柜类型 { get; set; }

        /// <summary>
        /// 混装GUID
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? 混装GUID { get; set; }

        /// <summary>
        /// 商品类型
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? 商品类型 { get; set; }

        /// <summary>
        /// 套装数量
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 套装数量 { get; set; }

        /// <summary>
        /// 装柜件数
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 装柜件数 { get; set; }

        /// <summary>
        /// 装柜数量
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 装柜数量 { get; set; }

        /// <summary>
        /// 国内价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 国内价格 { get; set; }

        /// <summary>
        /// 调整浮率
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 调整浮率 { get; set; }

        /// <summary>
        /// 进口价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 进口价格 { get; set; }

        /// <summary>
        /// 贴牌价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 贴牌价格 { get; set; }

        /// <summary>
        /// 单件装箱数
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 单件装箱数 { get; set; }

        /// <summary>
        /// 单件体积
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 单件体积 { get; set; }

        /// <summary>
        /// 合计装柜金额
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 合计装柜金额 { get; set; }

        /// <summary>
        /// 合计装柜体积
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 合计装柜体积 { get; set; }

        /// <summary>
        /// 运输成本
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 运输成本 { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? 备注 { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? 状态 { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? FGC_Creator { get; set; }

        /// <summary>
        /// 创建日期
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? FGC_CreateDate { get; set; }

        /// <summary>
        /// 最后修改人
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? FGC_LastModifier { get; set; }

        /// <summary>
        /// 最后修改日期
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? FGC_LastModifyDate { get; set; }
    }
}
