using System.Collections.Generic;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 仓库商品批量管理过滤条件DTO
    /// </summary>
    public class WarehouseProductBatchFilterDto
    {
        /// <summary>
        /// 商品编码（模糊匹配）
        /// </summary>
        public string? ProductCode { get; set; }

        /// <summary>
        /// 商品名称（模糊匹配）
        /// </summary>
        public string? ProductName { get; set; }

        /// <summary>
        /// 货号（模糊匹配）
        /// </summary>
        public string? ItemNumber { get; set; }

        /// <summary>
        /// 本地供应商编码（模糊匹配）
        /// </summary>
        public string? LocalSupplierCode { get; set; }

        /// <summary>
        /// 国内价格-最小值
        /// </summary>
        public decimal? DomesticPriceMin { get; set; }

        /// <summary>
        /// 国内价格-最大值
        /// </summary>
        public decimal? DomesticPriceMax { get; set; }

        /// <summary>
        /// 贴牌价格-最小值
        /// </summary>
        public decimal? OEMPriceMin { get; set; }

        /// <summary>
        /// 贴牌价格-最大值
        /// </summary>
        public decimal? OEMPriceMax { get; set; }

        /// <summary>
        /// 进口价格-最小值
        /// </summary>
        public decimal? ImportPriceMin { get; set; }

        /// <summary>
        /// 进口价格-最大值
        /// </summary>
        public decimal? ImportPriceMax { get; set; }

        /// <summary>
        /// 库存数量-最小值
        /// </summary>
        public int? StockQuantityMin { get; set; }

        /// <summary>
        /// 库存数量-最大值
        /// </summary>
        public int? StockQuantityMax { get; set; }

        /// <summary>
        /// 使用状态（null-全部，true-启用，false-禁用）
        /// </summary>
        public bool? IsActive { get; set; }

        /// <summary>
        /// 仓位代码（模糊匹配）
        /// </summary>
        public string? LocationCode { get; set; }

        /// <summary>
        /// 指定的商品编码集合（从外部页面传入）
        /// </summary>
        public List<string>? ProductCodes { get; set; }

        /// <summary>
        /// 页码（从1开始）
        /// </summary>
        public int PageIndex { get; set; } = 1;

        /// <summary>
        /// 每页数量（默认50）
        /// </summary>
        public int PageSize { get; set; } = 50;

        /// <summary>
        /// 排序字段
        /// </summary>
        public string? SortField { get; set; }

        /// <summary>
        /// 排序方向（asc/desc）
        /// </summary>
        public string? SortOrder { get; set; }
    }
}

