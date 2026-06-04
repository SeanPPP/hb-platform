using System;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 更新货柜信息DTO
    /// </summary>
    public class UpdateContainerDto
    {
        /// <summary>
        /// 实际到货日期
        /// </summary>
        public DateTime? 实际到货日期 { get; set; }

        /// <summary>
        /// 汇率
        /// </summary>
        public decimal? 汇率 { get; set; }

        /// <summary>
        /// 运费
        /// </summary>
        public decimal? 运费 { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string? 备注 { get; set; }

        /// <summary>
        /// 货柜状态
        /// </summary>
        public int? 状态 { get; set; }
    }
}



