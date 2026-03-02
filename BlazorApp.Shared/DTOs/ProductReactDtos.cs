using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// React专用：商品查询过滤DTO
    /// </summary>
    public class ProductReactFilterDto
    {
        /// <summary>
        /// 搜索关键词（商品名称、货号、条码）
        /// </summary>
        public string? Search { get; set; }
        
        /// <summary>
        /// 本地供应商代码过滤
        /// </summary>
        public string? LocalSupplierCode { get; set; }
        
        /// <summary>
        /// 是否启用状态过滤
        /// </summary>
        public bool? IsActive { get; set; }
        
        /// <summary>
        /// 是否特殊产品过滤
        /// </summary>
        public bool? IsSpecialProduct { get; set; }
        
        /// <summary>
        /// 仓库类别GUID过滤
        /// </summary>
        public string? WarehouseCategoryGUID { get; set; }
        public int? ProductType { get; set; }
        public string? UpdatedBy { get; set; }
        
        /// <summary>
        /// 最低价格过滤
        /// </summary>
        public decimal? MinPrice { get; set; }
        
        /// <summary>
        /// 最高价格过滤
        /// </summary>
        public decimal? MaxPrice { get; set; }
        
        /// <summary>
        /// 页码（从1开始）
        /// </summary>
        public int PageNumber { get; set; } = 1;
        
        /// <summary>
        /// 每页大小
        /// </summary>
        public int PageSize { get; set; } = 20;
        
        /// <summary>
        /// 排序字段
        /// </summary>
        public string? SortBy { get; set; }
        
        /// <summary>
        /// 排序方向：asc 或 desc
        /// </summary>
        public string SortOrder { get; set; } = "asc";
    }

    /// <summary>
    /// React专用：批量更新商品DTO
    /// </summary>
    public class BatchUpdateProductReactDto
    {
        /// <summary>
        /// 产品编码（必须）
        /// </summary>
        [Required(ErrorMessage = "产品编码不能为空")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 产品名称
        /// </summary>
        public string? ProductName { get; set; }

        /// <summary>
        /// 英文名称
        /// </summary>
        public string? EnglishName { get; set; }

        /// <summary>
        /// 零售价格
        /// </summary>
        public decimal? RetailPrice { get; set; }

        /// <summary>
        /// 采购价格
        /// </summary>
        public decimal? PurchasePrice { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool? IsActive { get; set; }

        /// <summary>
        /// 中包装数量
        /// </summary>
        public int? MiddlePackageQuantity { get; set; }

        /// <summary>
        /// 是否自动定价
        /// </summary>
        public bool? IsAutoPricing { get; set; }
    }

    /// <summary>
    /// React专用：批量操作结果
    /// </summary>
    public class BatchOperationReactResult
    {
        /// <summary>
        /// 成功数量
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// 失败数量
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// 错误信息列表
        /// </summary>
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// React专用：分页列表响应DTO
    /// </summary>
    public class PagedListReactDto<T>
    {
        /// <summary>
        /// 数据列表
        /// </summary>
        public List<T> Items { get; set; } = new();

        /// <summary>
        /// 总记录数
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// 当前页码
        /// </summary>
        public int PageNumber { get; set; }

        /// <summary>
        /// 每页大小
        /// </summary>
        public int PageSize { get; set; }
    }
}
