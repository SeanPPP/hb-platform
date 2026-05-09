using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 文本筛选类型枚举
    /// </summary>
    public enum TextFilterType
    {
        /// <summary>
        /// 等于
        /// </summary>
        equals = 0,
        /// <summary>
        /// 不等于
        /// </summary>
        notEquals = 1,
        /// <summary>
        /// 开头是
        /// </summary>
        startsWith = 2,
        /// <summary>
        /// 结尾是
        /// </summary>
        endsWith = 3,
        /// <summary>
        /// 包含
        /// </summary>
        contains = 4,
        /// <summary>
        /// 不包含
        /// </summary>
        notContains = 5
    }

    /// <summary>
    /// 数字筛选类型枚举
    /// </summary>
    public enum NumberFilterType
    {
        /// <summary>
        /// 等于
        /// </summary>
        equals = 0,
        /// <summary>
        /// 不等于
        /// </summary>
        notEquals = 1,
        /// <summary>
        /// 大于
        /// </summary>
        greaterThan = 2,
        /// <summary>
        /// 大于等于
        /// </summary>
        greaterThanOrEqual = 3,
        /// <summary>
        /// 小于
        /// </summary>
        lessThan = 4,
        /// <summary>
        /// 小于等于
        /// </summary>
        lessThanOrEqual = 5,
        /// <summary>
        /// 在范围内
        /// </summary>
        between = 6
    }

    /// <summary>
    /// React专用：商品查询过滤DTO
    /// </summary>
    public class ProductReactFilterDto
    {
        #region 文本字段高级筛选

        /// <summary>
        /// 货号筛选值
        /// </summary>
        public string? ItemNumber { get; set; }

        /// <summary>
        /// 货号筛选类型
        /// </summary>
        public TextFilterType ItemNumberFilterType { get; set; } = TextFilterType.contains;

        /// <summary>
        /// 条码筛选值
        /// </summary>
        public string? Barcode { get; set; }

        /// <summary>
        /// 条码筛选类型
        /// </summary>
        public TextFilterType BarcodeFilterType { get; set; } = TextFilterType.contains;

        /// <summary>
        /// 商品名称筛选值
        /// </summary>
        public string? ProductName { get; set; }

        /// <summary>
        /// 商品名称筛选类型
        /// </summary>
        public TextFilterType ProductNameFilterType { get; set; } = TextFilterType.contains;

        /// <summary>
        /// 更新人筛选值
        /// </summary>
        public string? UpdatedBy { get; set; }

        /// <summary>
        /// 更新人筛选类型
        /// </summary>
        public TextFilterType UpdatedByFilterType { get; set; } = TextFilterType.contains;

        #endregion

        #region 数字字段高级筛选

        /// <summary>
        /// 采购价最小值
        /// </summary>
        public decimal? PurchasePriceMin { get; set; }

        /// <summary>
        /// 采购价最大值
        /// </summary>
        public decimal? PurchasePriceMax { get; set; }

        /// <summary>
        /// 采购价筛选类型
        /// </summary>
        public NumberFilterType PurchasePriceFilterType { get; set; } = NumberFilterType.between;

        /// <summary>
        /// 零售价最小值
        /// </summary>
        public decimal? RetailPriceMin { get; set; }

        /// <summary>
        /// 零售价最大值
        /// </summary>
        public decimal? RetailPriceMax { get; set; }

        /// <summary>
        /// 零售价筛选类型
        /// </summary>
        public NumberFilterType RetailPriceFilterType { get; set; } = NumberFilterType.between;

        /// <summary>
        /// 中包数最小值
        /// </summary>
        public int? MiddlePackageQuantityMin { get; set; }

        /// <summary>
        /// 中包数最大值
        /// </summary>
        public int? MiddlePackageQuantityMax { get; set; }

        /// <summary>
        /// 中包数筛选类型
        /// </summary>
        public NumberFilterType MiddlePackageQuantityFilterType { get; set; } = NumberFilterType.between;

        #endregion

        #region 原有筛选参数
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

        /// <summary>
        /// 商品分类GUID过滤（支持多个）
        /// </summary>
        public List<string>? ProductCategoryGUIDs { get; set; }
        public int? ProductType { get; set; }

        #endregion

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

        /// <summary>
        /// 商品分类GUID
        /// </summary>
        public string? ProductCategoryGUID { get; set; }

        /// <summary>
        /// 本地供应商编码
        /// </summary>
        public string? LocalSupplierCode { get; set; }
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
