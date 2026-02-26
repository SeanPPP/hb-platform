using System;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 商品货位映射表，用于存储商品与货位的关联关系
    /// </summary>
    [SugarTable("ProductLocation")]
    public class ProductLocation:BaseEntity
    {
        /// <summary>
        /// 映射记录的全局唯一标识符
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, Length = 36)]
        public string Guid { get; set; } = string.Empty;

        /// <summary>
        /// 产品代码/标识符
        /// </summary>
        [SugarColumn(Length = 50, IsNullable = false)]
        public string? ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 货位GUID引用
        /// </summary>
        [SugarColumn(Length = 36, IsNullable = false)]
        public string? LocationGuid { get; set; } = string.Empty;
    }
}