using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.HqEntities
{
    /// <summary>
    /// 货柜单主表
    /// </summary>
    [SugarTable("CPT_RED_货柜单主表")]
    public class CPT_RED_货柜单主表Store
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
        /// 货柜编号
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? 货柜编号 { get; set; }

        /// <summary>
        /// 装柜日期
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? 装柜日期 { get; set; }

        /// <summary>
        /// 预计到岸日期
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? 预计到岸日期 { get; set; }

        /// <summary>
        /// 实际到货日期
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? 实际到货日期 { get; set; }

        /// <summary>
        /// 合计件数
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 合计件数 { get; set; }

        /// <summary>
        /// 合计数量
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 合计数量 { get; set; }

        /// <summary>
        /// 合计金额
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 合计金额 { get; set; }

        /// <summary>
        /// 总体积
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 总体积 { get; set; }

        /// <summary>
        /// 成本浮率
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 成本浮率 { get; set; }

        /// <summary>
        /// 汇率
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 汇率 { get; set; }

        /// <summary>
        /// 运费
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? 运费 { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? 备注 { get; set; }

        /// <summary>
        /// 备注2
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? 备注2 { get; set; }

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

        [Navigate(
            NavigateType.OneToMany,
            nameof(CPT_RED_货柜单详情表Store.主表GUID),
            nameof(HGUID)
        )]
        [SugarColumn(IsIgnore = true)]
        public List<CPT_RED_货柜单详情表Store> Details { get; set; } = new();
    }
}
