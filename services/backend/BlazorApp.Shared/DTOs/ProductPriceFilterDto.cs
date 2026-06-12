using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 商品价格管理专用查询过滤DTO
    /// 支持商品信息表与分店价格表的组合过滤
    /// </summary>
    public class ProductPriceFilterDto
    {
        /// <summary>
        /// 搜索关键词（商品名称、货号、条码）
        /// 示例: "iPhone"
        /// </summary>
        [StringLength(200, ErrorMessage = "搜索关键词长度不能超过200字符")]
        public string? Search { get; set; }

        /// <summary>
        /// 本地供应商代码过滤
        /// 示例: "SUP001"
        /// </summary>
        [StringLength(50, ErrorMessage = "供应商代码长度不能超过50字符")]
        public string? LocalSupplierCode { get; set; }

        /// <summary>
        /// 是否启用状态过滤
        /// 示例: true
        /// </summary>
        public bool? IsActive { get; set; }

        /// <summary>
        /// 是否特殊产品过滤
        /// 示例: false
        /// </summary>
        public bool? IsSpecialProduct { get; set; }

        /// <summary>
        /// 仓库类别GUID过滤
        /// 示例: "12345678-1234-1234-1234-123456789012"
        /// </summary>
        [StringLength(50, ErrorMessage = "仓库类别GUID长度不能超过50字符")]
        public string? WarehouseCategoryGUID { get; set; }

        /// <summary>
        /// 产品类型过滤
        /// 示例: 1
        /// </summary>
        public int? ProductType { get; set; }

        /// <summary>
        /// 更新人过滤
        /// 示例: "admin"
        /// </summary>
        [StringLength(50, ErrorMessage = "更新人长度不能超过50字符")]
        public string? UpdatedBy { get; set; }

        #region 商品主表价格区间过滤

        /// <summary>
        /// 商品主表最低进货价过滤
        /// 使用 BETWEEN 语法,支持闭区间
        /// 示例: 0
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "最低进货价不能为负数")]
        public decimal? ProductPurchasePriceMin { get; set; }

        /// <summary>
        /// 商品主表最高进货价过滤
        /// 使用 BETWEEN 语法,支持闭区间
        /// 示例: 999999.99
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "最高进货价不能为负数")]
        public decimal? ProductPurchasePriceMax { get; set; }

        /// <summary>
        /// 商品主表最低零售价过滤
        /// 使用 BETWEEN 语法,支持闭区间
        /// 示例: 0
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "最低零售价不能为负数")]
        public decimal? ProductRetailPriceMin { get; set; }

        /// <summary>
        /// 商品主表最高零售价过滤
        /// 使用 BETWEEN 语法,支持闭区间
        /// 示例: 999999.99
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "最高零售价不能为负数")]
        public decimal? ProductRetailPriceMax { get; set; }

        #endregion

        #region 分店价格表过滤

        /// <summary>
        /// 分店代码数组过滤
        /// 与商品主表执行 INNER JOIN,确保返回结果只包含指定分店有价格记录的商品
        /// 支持多分店同时过滤
        /// 示例: ["STORE001", "STORE002"]
        /// </summary>
        [Required(ErrorMessage = "分店代码数组不能为空")]
        [MinLength(1, ErrorMessage = "至少需要提供一个分店代码")]
        public List<string> StoreCodes { get; set; } = new();

        /// <summary>
        /// 分店价格表最低进货价过滤
        /// 使用 BETWEEN 语法,支持闭区间
        /// 示例: 0
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "分店最低进货价不能为负数")]
        public decimal? StorePurchasePriceMin { get; set; }

        /// <summary>
        /// 分店价格表最高进货价过滤
        /// 使用 BETWEEN 语法,支持闭区间
        /// 示例: 999999.99
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "分店最高进货价不能为负数")]
        public decimal? StorePurchasePriceMax { get; set; }

        /// <summary>
        /// 分店价格表最低零售价过滤
        /// 使用 BETWEEN 语法,支持闭区间
        /// 示例: 0
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "分店最低零售价不能为负数")]
        public decimal? StoreRetailPriceMin { get; set; }

        /// <summary>
        /// 分店价格表最高零售价过滤
        /// 使用 BETWEEN 语法,支持闭区间
        /// 示例: 999999.99
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "分店最高零售价不能为负数")]
        public decimal? StoreRetailPriceMax { get; set; }

        /// <summary>
        /// 分店价格最低折扣率过滤
        /// 使用 BETWEEN 语法,支持闭区间
        /// 示例: 0
        /// </summary>
        [Range(0, 1, ErrorMessage = "折扣率必须在0-1之间")]
        public decimal? StoreDiscountRateMin { get; set; }

        /// <summary>
        /// 分店价格最高折扣率过滤
        /// 使用 BETWEEN 语法,支持闭区间
        /// 示例: 1
        /// </summary>
        [Range(0, 1, ErrorMessage = "折扣率必须在0-1之间")]
        public decimal? StoreDiscountRateMax { get; set; }

        /// <summary>
        /// 分店价格是否启用过滤
        /// 示例: true
        /// </summary>
        public bool? StoreIsActive { get; set; }

        /// <summary>
        /// 分店价格是否自动定价过滤
        /// 示例: false
        /// </summary>
        public bool? StoreIsAutoPricing { get; set; }

        #endregion

        #region 分页和排序

        /// <summary>
        /// 页码（从1开始）
        /// 示例: 1
        /// </summary>
        [Range(1, int.MaxValue, ErrorMessage = "页码必须大于0")]
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// 每页大小
        /// 支持值: 20, 50, 100, 200, 500, 1000
        /// 示例: 50
        /// </summary>
        [Range(20, 1000, ErrorMessage = "每页大小必须在20-1000之间")]
        public int PageSize { get; set; } = 50;

        /// <summary>
        /// 排序字段
        /// 支持字段: ProductCode, ProductName, ProductPurchasePrice, ProductRetailPrice,
        /// StorePurchasePrice, StoreRetailPrice, StoreDiscountRate, CreatedAt, UpdatedAt
        /// 示例: "StoreRetailPrice"
        /// </summary>
        [StringLength(50, ErrorMessage = "排序字段长度不能超过50字符")]
        public string? SortBy { get; set; }

        /// <summary>
        /// 排序方向: asc 或 desc
        /// 示例: "asc"
        /// </summary>
        [RegularExpression("^(asc|desc)$", ErrorMessage = "排序方向必须是 asc 或 desc")]
        public string SortOrder { get; set; } = "asc";

        #endregion
    }

    /// <summary>
    /// 商品价格列表项DTO
    /// 包含商品主表全部字段 + 对应分店价格字段
    /// </summary>
    public class ProductPriceListItemDto
    {
        #region 商品主表字段

        /// <summary>
        /// 商品UUID
        /// </summary>
        public string UUID { get; set; } = string.Empty;

        /// <summary>
        /// 商品编码
        /// </summary>
        public string? ProductCode { get; set; }

        /// <summary>
        /// 商品货号
        /// </summary>
        public string? ItemNumber { get; set; }

        /// <summary>
        /// 条码
        /// </summary>
        public string? Barcode { get; set; }

        /// <summary>
        /// 商品名称
        /// </summary>
        public string ProductName { get; set; } = string.Empty;

        /// <summary>
        /// 英文名称
        /// </summary>
        public string? EnglishName { get; set; }

        /// <summary>
        /// 本地供应商代码
        /// </summary>
        public string? LocalSupplierCode { get; set; }

        /// <summary>
        /// 产品类型
        /// </summary>
        public int? ProductType { get; set; }

        /// <summary>
        /// 中包装数量
        /// </summary>
        public int? MiddlePackageQuantity { get; set; }

        /// <summary>
        /// 商品主表进货价
        /// </summary>
        public decimal? ProductPurchasePrice { get; set; }

        /// <summary>
        /// 商品主表零售价
        /// </summary>
        public decimal? ProductRetailPrice { get; set; }

        /// <summary>
        /// 是否自动定价
        /// </summary>
        public bool IsAutoPricing { get; set; }

        /// <summary>
        /// 是否特殊产品
        /// </summary>
        public bool IsSpecialProduct { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 产品图片
        /// </summary>
        public string? ProductImage { get; set; }

        /// <summary>
        /// 仓库类别GUID
        /// </summary>
        public string? WarehouseCategoryGUID { get; set; }

        /// <summary>
        /// 更新人
        /// </summary>
        public string? UpdatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        #endregion

        #region 分店价格字段

        /// <summary>
        /// 分店代码
        /// </summary>
        public string? StoreCode { get; set; }

        /// <summary>
        /// 分店名称
        /// </summary>
        public string? StoreName { get; set; }

        /// <summary>
        /// 分店商品编码
        /// </summary>
        public string? StoreProductCode { get; set; }

        /// <summary>
        /// 供应商编码
        /// </summary>
        public string? SupplierCode { get; set; }

        /// <summary>
        /// 供应商名称
        /// </summary>
        public string? SupplierName { get; set; }

        /// <summary>
        /// 分店进货价
        /// </summary>
        public decimal? StorePurchasePrice { get; set; }

        /// <summary>
        /// 分店零售价
        /// </summary>
        public decimal? StoreRetailPrice { get; set; }

        /// <summary>
        /// 折扣率
        /// </summary>
        public decimal? StoreDiscountRate { get; set; }

        /// <summary>
        /// 分店价格是否启用
        /// </summary>
        public bool? StoreIsActive { get; set; }

        /// <summary>
        /// 分店价格是否自动定价
        /// </summary>
        public bool? StoreIsAutoPricing { get; set; }

        #endregion
    }

    /// <summary>
    /// 分页列表响应DTO
    /// </summary>
    public class PagedProductPriceListDto
    {
        /// <summary>
        /// 数据列表
        /// </summary>
        public List<ProductPriceListItemDto> Items { get; set; } = new();

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
