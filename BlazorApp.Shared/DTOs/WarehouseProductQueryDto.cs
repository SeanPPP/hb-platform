using SqlSugar;
using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 仓库商品查询条件DTO
    /// </summary>
    public class WarehouseProductQueryDto
    {
        /// <summary>
        /// 页码
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// 每页数量
        /// </summary>
        public int PageSize { get; set; } = 20;

        /// <summary>
        /// 关键字搜索（商品名称、商品编码、条码）
        /// </summary>
        public string? Keyword { get; set; }

        /// <summary>
        /// 分类GUID
        /// </summary>
        public string? CategoryGUID { get; set; }

        /// <summary>
        /// 是否包含子分类查询
        /// </summary>
        public bool IncludeSubCategories { get; set; } = true;

        //// <summary>
        /// 单件体积
        /// </summary>
        public decimal? Volume { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool? IsActive { get; set; }

        /// <summary>
        /// 最小库存数量
        /// </summary>
        public int? MinStockQuantity { get; set; }

        /// <summary>
        /// 最大库存数量
        /// </summary>
        public int? MaxStockQuantity { get; set; }

        /// <summary>
        /// 最小价格
        /// </summary>
        public decimal? MinPrice { get; set; }

        /// <summary>
        /// 最大价格
        /// </summary>
        public decimal? MaxPrice { get; set; }

        /// <summary>
        /// 价格类型（DomesticPrice/OEMPrice/ImportPrice）
        /// </summary>
        public string PriceType { get; set; } = "DomesticPrice";

        /// <summary>
        /// 是否有库存预警
        /// </summary>
        public bool? HasStockAlert { get; set; }

        /// <summary>
        /// 排序字段
        /// </summary>
        public string SortBy { get; set; } = "货号";

        /// <summary>
        /// 是否降序排序
        /// </summary>
        public bool SortDescending { get; set; } = false;

        /// <summary>
        /// 仓库位置GUID列表（多仓库支持）
        /// </summary>
        public List<string>? LocationGuids { get; set; }
    }
}
