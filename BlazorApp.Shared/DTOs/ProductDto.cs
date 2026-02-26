using System;
using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 产品数据传输对象，用于在应用各层之间传输产品数据
    /// </summary>
    public class ProductDto
    {
        /// <summary>
        /// 产品类别GUID
        /// </summary>
        public string? ProductCategoryGUID { get; set; }

        /// <summary>
        /// 本地供应商代码
        /// </summary>
        [StringLength(50, ErrorMessage = "本地供应商代码不能超过50个字符")]
        public string? LocalSupplierCode { get; set; }

        /// <summary>
        /// 产品代码
        /// </summary>
        [Required(ErrorMessage = "产品代码不能为空")]
        [StringLength(50, ErrorMessage = "产品代码不能超过50个字符")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 项目编号
        /// </summary>
        [StringLength(50, ErrorMessage = "项目编号不能超过50个字符")]
        public string? ItemNumber { get; set; }

        /// <summary>
        /// 条码
        /// </summary>
        [StringLength(50, ErrorMessage = "条码不能超过50个字符")]
        public string? Barcode { get; set; }

        /// <summary>
        /// 产品名称
        /// </summary>
        [Required(ErrorMessage = "产品名称不能为空")]
        [StringLength(200, ErrorMessage = "产品名称不能超过200个字符")]
        public string ProductName { get; set; } = string.Empty;

        /// <summary>
        /// 产品类型
        /// </summary>
        public int? ProductType { get; set; }

        /// <summary>
        /// 中包装数量
        /// </summary>
        public int? MiddlePackageQuantity { get; set; }

        /// <summary>
        /// 采购价格
        /// </summary>
        public decimal? PurchasePrice { get; set; }

        /// <summary>
        /// 零售价格
        /// </summary>
        public decimal? RetailPrice { get; set; }

        /// <summary>
        /// 是否自动定价
        /// </summary>
        public bool IsAutoPricing { get; set; } = false;

        /// <summary>
        /// 产品图片路径
        /// </summary>
        [StringLength(500, ErrorMessage = "产品图片路径不能超过500个字符")]
        public string? ProductImage { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 是否特殊产品
        /// </summary>
        public bool IsSpecialProduct { get; set; } = false;

        /// <summary>
        /// 仓库类别GUID
        /// </summary>
        public string? WarehouseCategoryGUID { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }

    /// <summary>
    /// 创建产品数据传输对象，用于创建新的产品
    /// </summary>
    public class CreateProductDto
    {
        /// <summary>
        /// 产品类别GUID
        /// </summary>
        public string? ProductCategoryGUID { get; set; }

        /// <summary>
        /// 本地供应商代码
        /// </summary>
        [StringLength(50, ErrorMessage = "本地供应商代码不能超过50个字符")]
        public string? LocalSupplierCode { get; set; }

        /// <summary>
        /// 产品代码
        /// </summary>
        [Required(ErrorMessage = "产品代码不能为空")]
        [StringLength(50, ErrorMessage = "产品代码不能超过50个字符")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 项目编号
        /// </summary>
        [StringLength(50, ErrorMessage = "项目编号不能超过50个字符")]
        public string? ItemNumber { get; set; }

        /// <summary>
        /// 条码
        /// </summary>
        [StringLength(50, ErrorMessage = "条码不能超过50个字符")]
        public string? Barcode { get; set; }

        /// <summary>
        /// 产品名称
        /// </summary>
        [Required(ErrorMessage = "产品名称不能为空")]
        [StringLength(200, ErrorMessage = "产品名称不能超过200个字符")]
        public string ProductName { get; set; } = string.Empty;

        /// <summary>
        /// 产品类型
        /// </summary>
        public int? ProductType { get; set; }

        /// <summary>
        /// 中包装数量
        /// </summary>
        public int? MiddlePackageQuantity { get; set; }

        /// <summary>
        /// 采购价格
        /// </summary>
        public decimal? PurchasePrice { get; set; }

        /// <summary>
        /// 零售价格
        /// </summary>
        public decimal? RetailPrice { get; set; }

        /// <summary>
        /// 是否自动定价
        /// </summary>
        public bool IsAutoPricing { get; set; } = false;

        /// <summary>
        /// 产品图片路径
        /// </summary>
        [StringLength(500, ErrorMessage = "产品图片路径不能超过500个字符")]
        public string? ProductImage { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 是否特殊产品
        /// </summary>
        public bool IsSpecialProduct { get; set; } = false;

        /// <summary>
        /// 仓库类别GUID
        /// </summary>
        public string? WarehouseCategoryGUID { get; set; }
    }

    /// <summary>
    /// 更新产品数据传输对象，用于更新现有产品
    /// </summary>
    public class UpdateProductDto
    {
        /// <summary>
        /// 产品类别GUID
        /// </summary>
        public string? ProductCategoryGUID { get; set; }

        /// <summary>
        /// 本地供应商代码
        /// </summary>
        [StringLength(50, ErrorMessage = "本地供应商代码不能超过50个字符")]
        public string? LocalSupplierCode { get; set; }

        /// <summary>
        /// 产品代码
        /// </summary>
        [StringLength(50, ErrorMessage = "产品代码不能超过50个字符")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 项目编号
        /// </summary>
        [StringLength(50, ErrorMessage = "项目编号不能超过50个字符")]
        public string? ItemNumber { get; set; }

        /// <summary>
        /// 条码
        /// </summary>
        [StringLength(50, ErrorMessage = "条码不能超过50个字符")]
        public string? Barcode { get; set; }

        /// <summary>
        /// 产品名称
        /// </summary>
        [Required(ErrorMessage = "产品名称不能为空")]
        [StringLength(200, ErrorMessage = "产品名称不能超过200个字符")]
        public string ProductName { get; set; } = string.Empty;

        /// <summary>
        /// 产品类型
        /// </summary>
        public int? ProductType { get; set; }

        /// <summary>
        /// 中包装数量
        /// </summary>
        public int? MiddlePackageQuantity { get; set; }

        /// <summary>
        /// 采购价格
        /// </summary>
        public decimal? PurchasePrice { get; set; }

        /// <summary>
        /// 零售价格
        /// </summary>
        public decimal? RetailPrice { get; set; }

        /// <summary>
        /// 是否自动定价
        /// </summary>
        public bool IsAutoPricing { get; set; } = false;

        /// <summary>
        /// 产品图片路径
        /// </summary>
        [StringLength(500, ErrorMessage = "产品图片路径不能超过500个字符")]
        public string? ProductImage { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 是否特殊产品
        /// </summary>
        public bool IsSpecialProduct { get; set; } = false;

        /// <summary>
        /// 仓库类别GUID
        /// </summary>
        public string? WarehouseCategoryGUID { get; set; }
    }

    /// <summary>
    /// 产品过滤数据传输对象，用于查询时的过滤条件
    /// </summary>
    public class ProductFilterDto
    {
        /// <summary>
        /// 产品名称过滤条件
        /// </summary>
        public string? ProductName { get; set; }

        /// <summary>
        /// 产品代码过滤条件
        /// </summary>
        public string? ProductCode { get; set; }

        /// <summary>
        /// 条码过滤条件
        /// </summary>
        public string? Barcode { get; set; }

        /// <summary>
        /// 是否启用状态过滤条件
        /// </summary>
        public bool? IsActive { get; set; }

        /// <summary>
        /// 是否特殊产品过滤条件
        /// </summary>
        public bool? IsSpecialProduct { get; set; }

        /// <summary>
        /// 最低价格过滤条件
        /// </summary>
        public decimal? MinPrice { get; set; }

        /// <summary>
        /// 最高价格过滤条件
        /// </summary>
        public decimal? MaxPrice { get; set; }

        /// <summary>
        /// 产品类别GUID过滤条件
        /// </summary>
        public string? ProductCategoryGUID { get; set; }

        /// <summary>
        /// 仓库类别GUID过滤条件
        /// </summary>
        public string? WarehouseCategoryGUID { get; set; }

        /// <summary>
        /// 页码（从1开始）
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// 每页大小
        /// </summary>
        public int PageSize { get; set; } = 10;

        /// <summary>
        /// 排序字段
        /// </summary>
        public string? SortBy { get; set; } = "ProductName";

        /// <summary>
        /// 是否降序排列
        /// </summary>
        public bool SortDescending { get; set; } = false;
    }
}
