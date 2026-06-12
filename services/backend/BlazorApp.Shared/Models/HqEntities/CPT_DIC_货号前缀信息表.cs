using SqlSugar;
using System;
using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.Models.HqEntities
{
    /// <summary>
    /// HB货号前缀信息表
    /// </summary>
    [SugarTable("CPT_DIC_货号前缀信息表")]
    public class CPT_DIC_货号前缀信息表
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
        /// 供应商编码
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 50)]
        [Required(ErrorMessage = "供应商编码不能为空")]
        [Display(Name = "供应商编码")]
        public string 供应商编码 { get; set; } = string.Empty;

        /// <summary>
        /// HB货号前缀码（如：HB、YW、GZ等）
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 10)]
        [Required(ErrorMessage = "HB货号前缀码不能为空")]
        [Display(Name = "HB货号前缀码")]
        public string HB货号前缀码 { get; set; } = string.Empty;

        /// <summary>
        /// 前缀描述/说明
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 200)]
        [Display(Name = "前缀描述")]
        public string? 前缀描述 { get; set; }
       
        /// <summary>
        /// 最后修改日期
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? FGC_LastModifyDate { get; set; }

    }
}
