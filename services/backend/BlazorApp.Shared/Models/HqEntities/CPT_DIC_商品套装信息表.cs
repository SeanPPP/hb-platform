using SqlSugar;
using System;
using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.Models.HqEntities
{
    /// <summary>
    /// 商品小货号信息表
    /// </summary>
    [SugarTable("CPT_DIC_商品套装信息表")]
    public class CPT_DIC_商品套装信息表
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int ID { get; set; }

        /// <summary>
        /// 全局唯一标识符
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? HGUID { get; set; }

        /// <summary>
        /// 商品编码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "商品编码")]
        public string? 商品编码 { get; set; }

        /// <summary>
        /// 商品小货号
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "商品小货号")]
        public string? 商品小货号 { get; set; }

        /// <summary>
        /// 条形码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "条形码")]
        public string? 条形码 { get; set; }

        /// <summary>
        /// 国内价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "国内价格")]
        public decimal? 国内价格 { get; set; }

        /// <summary>
        /// 进口价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "进口价格")]
        public decimal? 进口价格 { get; set; }

        /// <summary>
        /// 贴牌价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "贴牌价格")]
        public decimal? 贴牌价格 { get; set; }

        /// <summary>
        /// 单件装箱数
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "单件装箱数")]
        public int? 单件装箱数 { get; set; }

        /// <summary>
        /// 单件体积
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "单件体积")]
        public decimal? 单件体积 { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 1000)]
        [Display(Name = "备注")]
        public string? 备注 { get; set; }

        /// <summary>
        /// 使用状态
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "使用状态")]
        public int? 使用状态 { get; set; }

    }
}
