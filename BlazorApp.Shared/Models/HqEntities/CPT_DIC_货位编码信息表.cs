using SqlSugar;
using System;

namespace BlazorApp.Shared.Models.HqEntities
{
    /// <summary>
    /// 货位编码信息表
    /// </summary>
    [SugarTable("CPT_DIC_货位编码信息表")]
    public class CPT_DIC_货位编码信息表
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
        /// 货位类型
        /// </summary>
        [SugarColumn(IsNullable = true)] 
        public int? 货位类型 { get; set; }

        /// <summary>
        /// 货位编码
        /// </summary>
        [SugarColumn(IsNullable = true)] 
        public string? 货位编码 { get; set; }

        //导航 货位配货信息表
        [Navigate(NavigateType.OneToOne, nameof(CPT_RED_货位配货信息表.货位编码), nameof(货位编码))]
        public CPT_RED_货位配货信息表? PickLocation { get; set; }
        //导航 货位存货信息表
        [Navigate(NavigateType.OneToMany, nameof(CPT_RED_货位存货信息表.货位编码), nameof(货位编码))]
        public List<CPT_RED_货位存货信息表>? StockLocation { get; set; }

        /// <summary>
        /// 货位条形码
        /// </summary>
        [SugarColumn(IsNullable = true)] 
        public string? 货位条形码 { get; set; }

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
        public string? FGC_CreateDate { get; set; }

        /// <summary>
        /// 最后修改人
        /// </summary>
        [SugarColumn(IsNullable = true)] 
        public string? FGC_LastModifier { get; set; }

        /// <summary>
        /// 最后修改日期
        /// </summary>
        [SugarColumn(IsNullable = true)] 
        public string? FGC_LastModifyDate { get; set; }

        /// <summary>
        /// 更新帮助
        /// </summary>
        [SugarColumn(IsNullable = true)] 
        public string? FGC_UpdateHelp { get; set; }

        /// <summary>
        /// 行版本
        /// </summary>
        [SugarColumn(IsNullable = true)] 
        public string? FGC_Rowversion { get; set; }
    }
}
