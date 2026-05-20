using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 货位编码信息表，存储仓库中货位的基本信息
    /// </summary>
    [SugarTable("Location")]
    public class Location : BaseEntity
    {
        /// <summary>
        /// 主键LocationGuid  /// 货位全局唯一标识符
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
        public string LocationGuid { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 货位类型分类（1: 配货位, 2: 存货位）
        /// </summary>
        [SugarColumn(IsNullable = true), Length(20, 20)]
        public int? LocationType { get; set; }

        /// <summary>
        /// 货位代码/标识符
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? LocationCode { get; set; }

        /// <summary>
        /// 货位条码，用于扫描识别
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? LocationBarcode { get; set; }

        /// <summary>
        /// 状态标识（0: 禁用, 1: 启用）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? Status { get; set; }

        /// <summary>
        /// 关联的产品列表（多对多导航属性）
        /// </summary>
        [Navigate(
            typeof(ProductLocation),
            nameof(ProductLocation.LocationGuid),
            nameof(ProductLocation.ProductCode)
        )]
        public List<Product>? products { get; set; }
    }
}
