using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 创建分店订单请求DTO
    /// </summary>
    public class CreateStoreOrderRequest
    {
        /// <summary>
        /// 分店GUID
        /// </summary>
        [Required(ErrorMessage = "分店GUID不能为空")]
        public string StoreGUID { get; set; } = string.Empty;

        /// <summary>
        /// 订单名称（可选）
        /// </summary>
        public string? CartName { get; set; }


        /// <summary>
        /// 备注（可选）
        /// </summary>
        public string? Remarks { get; set; }
    }

    /// <summary>
    /// Excel导入商品项
    /// </summary>
    public class ExcelImportItem
    {
        /// <summary>
        /// 货号（必须）
        /// </summary>
        [Required(ErrorMessage = "货号不能为空")]
        public string ItemNumber { get; set; } = string.Empty;

        /// <summary>
        /// 数量（必须）
        /// </summary>
        [Required(ErrorMessage = "数量不能为空")]
        [Range(1, int.MaxValue, ErrorMessage = "数量必须大于0")]
        public int Quantity { get; set; }

        /// <summary>
        /// 价格（可选，如果不提供则使用商品默认价格）
        /// </summary>
        public decimal? Price { get; set; }
    }

    /// <summary>
    /// Excel导入请求DTO
    /// </summary>
    public class ExcelImportRequest
    {
        /// <summary>
        /// 订单GUID
        /// </summary>
        [Required(ErrorMessage = "订单GUID不能为空")]
        public string CartGUID { get; set; } = string.Empty;

        /// <summary>
        /// 导入的商品列表
        /// </summary>
        [Required(ErrorMessage = "商品列表不能为空")]
        public List<ExcelImportItem> Items { get; set; } = new();

        /// <summary>
        /// 是否清除原有商品
        /// </summary>
        public bool ClearExistingItems { get; set; } = true;
    }

    /// <summary>
    /// Excel导入结果DTO
    /// </summary>
    public class ExcelImportResult
    {
        /// <summary>
        /// 总数量
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 成功数量
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// 失败数量
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// 成功添加的商品
        /// </summary>
        public List<CartItemDto> SuccessItems { get; set; } = new();

        /// <summary>
        /// 失败的商品信息
        /// </summary>
        public List<ExcelImportError> Errors { get; set; } = new();
    }

    /// <summary>
    /// Excel导入错误信息
    /// </summary>
    public class ExcelImportError
    {
        /// <summary>
        /// 货号
        /// </summary>
        public string ItemNumber { get; set; } = string.Empty;

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// 批量查询商品请求DTO
    /// </summary>
    public class BatchSearchProductsRequest
    {
        /// <summary>
        /// 货号列表
        /// </summary>
        [Required(ErrorMessage = "货号列表不能为空")]
        public List<string> ItemNumbers { get; set; } = new();
    }

    /// <summary>
    /// excel导入批量添加商品请求DTO
    /// </summary>
    public class ExcelImportAddItemsRequest
    {
        /// <summary>
        /// 订单GUID
        /// </summary>
        [Required(ErrorMessage = "订单GUID不能为空")]
        public string CartGUID { get; set; } = string.Empty;

        /// <summary>
        /// 要添加的商品列表
        /// </summary>
        [Required(ErrorMessage = "商品列表不能为空")]
        public List<BatchAddItem> Items { get; set; } = new();
    }
    /// <summary>
    /// 批量添加商品请求DTO
    /// </summary>
    public class BatchAddItemsRequest
    {
        /// <summary>
        /// 订单GUID
        /// </summary>
        [Required(ErrorMessage = "订单GUID不能为空")]
        public string CartGUID { get; set; } = string.Empty;

        /// <summary>
        /// 要添加的商品列表
        /// </summary>
        [Required(ErrorMessage = "商品列表不能为空")]
        public List<WarehouseProductListDto> Items { get; set; } = new();
    }

    /// <summary>
    /// 批量添加商品项
    /// </summary>
    public class BatchAddItem
    {
        /// <summary>
        /// 商品代码
        /// </summary>
        [Required(ErrorMessage = "商品代码不能为空")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 数量
        /// </summary>
        [Required(ErrorMessage = "数量不能为空")]
        [Range(1, int.MaxValue, ErrorMessage = "数量必须大于0")]
        public int Quantity { get; set; }

        /// <summary>
        /// 实际数量（可选）
        /// </summary>
        public int? ActualQuantity { get; set; }

        /// <summary>
        /// 实际价格（可选）
        /// </summary>
        public decimal? ActualPrice { get; set; }
    }

    /// <summary>
    /// 批量添加结果DTO
    /// </summary>
    public class BatchAddResult
    {
        /// <summary>
        /// 总数量
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 成功数量
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// 失败数量
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// 成功添加的商品
        /// </summary>
        public List<CartItemDto> SuccessItems { get; set; } = new();

        /// <summary>
        /// 失败的商品信息
        /// </summary>
        public List<BatchAddError> Errors { get; set; } = new();
    }

    /// <summary>
    /// 批量添加错误信息
    /// </summary>
    public class BatchAddError
    {
        /// <summary>
        /// 商品代码
        /// </summary>
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// 商品搜索结果DTO（用于产品选择）
    /// </summary>
    public class ProductSearchResult
    {
        /// <summary>
        /// 商品代码
        /// </summary>
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 货号
        /// </summary>
        public string? ItemNumber { get; set; }

        /// <summary>
        /// 商品名称
        /// </summary>
        public string? ProductName { get; set; }

        /// <summary>
        /// 商品分类名称
        /// </summary>
        public string? CategoryName { get; set; }

        /// <summary>
        /// 单价
        /// </summary>
        public decimal UnitPrice { get; set; }

        /// <summary>
        /// RRP零售价
        /// </summary>
        public decimal? RRPPrice { get; set; }

        /// <summary>
        /// 最小订货量
        /// </summary>
        public int MinOrderQuantity { get; set; } = 1;

        /// <summary>
        /// 库存数量
        /// </summary>
        public int StockQuantity { get; set; }

        /// <summary>
        /// 商品图片URL
        /// </summary>
        public string? ProductImage { get; set; }

        /// <summary>
        /// 货位编码
        /// </summary>
        public string? LocationCode { get; set; }

        /// <summary>
        /// 条码
        /// </summary>
        public string? Barcode { get; set; }
    }

    /// <summary>
    /// 商品搜索请求DTO
    /// </summary>
    public class ProductSearchRequest
    {
        /// <summary>
        /// 关键字（商品名称、货号、条码等）
        /// </summary>
        public string? Keyword { get; set; }

        /// <summary>
        /// 分类GUID
        /// </summary>
        public string? CategoryGUID { get; set; }

        /// <summary>
        /// 页码
        /// </summary>
        public int Page { get; set; } = 1;

        /// <summary>
        /// 每页数量
        /// </summary>
        public int PageSize { get; set; } = 20;

        /// <summary>
        /// 是否只显示有库存的商品
        /// </summary>
        public bool OnlyInStock { get; set; } = false;
    }

    /// <summary>
    /// 商品搜索响应DTO
    /// </summary>
    public class ProductSearchResponse
    {
        /// <summary>
        /// 商品列表
        /// </summary>
        public List<ProductSearchResult> Products { get; set; } = new();

        /// <summary>
        /// 总数量
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 当前页码
        /// </summary>
        public int CurrentPage { get; set; }

        /// <summary>
        /// 每页数量
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages { get; set; }
    }


}