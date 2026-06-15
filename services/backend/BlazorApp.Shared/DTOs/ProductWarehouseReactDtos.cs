using System;
using System.Collections.Generic;
using BlazorApp.Shared.Models;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 前端检测请求项（仅限 Product 与 WarehouseProduct 相关字段）
    /// </summary>
    public class DetectionItemDto
    {
        public string? ProductCode { get; set; }
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
    }

    /// <summary>
    /// 检测结果项（包含匹配来源与仓库旧数据摘要）
    /// </summary>
    public class DetectionResultDto
    {
        public string? ProductCode { get; set; }
        public string? ItemNumber { get; set; }
        public bool Exists { get; set; }
        /// <summary>
        /// 匹配来源：product_code | item_number | both | none
        /// </summary>
        public string MatchType { get; set; } = "none";

        public string? ProductName { get; set; }
        public string? EnglishName { get; set; }
        public decimal? WarehouseDomesticPrice { get; set; }
        public decimal? WarehouseOEMPrice { get; set; }
        public decimal? WarehouseImportPrice { get; set; }
        public decimal? WarehouseVolume { get; set; }
        public decimal? PackingQuantity { get; set; }
        public decimal? DomesticPrice { get; set; }
        public decimal? DomesticOEMPrice { get; set; }
        public decimal? DomesticImportPrice { get; set; }
        public bool? WarehouseIsActive { get; set; }
    }

    /// <summary>
    /// 批量更新请求项
    /// </summary>
    public class UpdateItemDto
    {
        public string? ProductCode { get; set; }
        public string? ItemNumber { get; set; }
        public decimal? DomesticPrice { get; set; }
        public decimal? OEMPrice { get; set; }
        public decimal? ImportPrice { get; set; }
        public decimal? Volume { get; set; }
        public bool? IsActive { get; set; } = true;
    }

    /// <summary>
    /// 批量创建请求项（仅 Product 与 WarehouseProduct；支持套装商品写入 ProductSetCode）
    /// </summary>
    public class CreateItemDto
    {
        /// <summary>
        /// 商品编码，不能为空；如前端未提供，后端将自动生成 UUID7
        /// </summary>
        public string? ProductCode { get; set; }
        public string ItemNumber { get; set; } = string.Empty;
        public string? Barcode { get; set; }
        public string ChineseName { get; set; } = string.Empty;
        public string? EnglishName { get; set; }
        public decimal? DomesticPrice { get; set; }
        public decimal OEMPrice { get; set; }
        public decimal ImportPrice { get; set; }
        public decimal? Volume { get; set; }
        public string? ImageUrl { get; set; }
        /// <summary>
        /// 商品类型：0=普通商品，1=套装商品，2=多码商品
        /// </summary>
        public int? ProductType { get; set; }
        /// <summary>
        /// 是否套装商品；若为 true，需从 DomesticSetProduct 更新到 ProductSetCode
        /// </summary>
        public bool IsSetProduct { get; set; } = false;
    }

    /// <summary>
    /// 批量操作结果
    /// </summary>
    public class BatchOperationResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> SkippedItems { get; set; } = new List<string>();
    }

    /// <summary>
    /// 商品类型枚举
    /// </summary>
    public enum ProductTypeEnum
    {
        /// <summary>
        /// 普通商品
        /// </summary>
        Normal = 0,
        /// <summary>
        /// 套装商品
        /// </summary>
        Set = 1,
        /// <summary>
        /// 多码商品
        /// </summary>
        MultiCode = 2
    }

    /// <summary>
    /// 套装类型枚举
    /// </summary>
    public enum SetTypeEnum
    {
        /// <summary>
        /// 组合套装
        /// </summary>
        Combination = 1,
        /// <summary>
        /// 固定套装
        /// </summary>
        Fixed = 2,
        /// <summary>
        /// 变量套装
        /// </summary>
        Variable = 3
    }

    /// <summary>
    /// 套装子项 DTO
    /// </summary>
    public class SetItemDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public string ItemNumber { get; set; } = string.Empty;
        public string? Barcode { get; set; }
        public decimal Quantity { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? RetailPrice { get; set; }
    }

    /// <summary>
    /// 多码子项 DTO
    /// </summary>
    public class MultiCodeItemDto
    {
        public string Barcode { get; set; } = string.Empty;
        public decimal? RetailPrice { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? DiscountRate { get; set; }
        public bool AutoPricing { get; set; } = false;
        public bool IsSpecialProduct { get; set; } = false;
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// 分店零售价格 DTO
    /// </summary>
    public class StorePriceDto
    {
        public int StoreId { get; set; }
        public string? SupplierCode { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? RetailPrice { get; set; }
        public decimal? DiscountRate { get; set; }
        public bool AutoPricing { get; set; } = false;
        public bool IsSpecialProduct { get; set; } = false;
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// 新建单个仓库商品请求 DTO
    /// </summary>
    public class CreateSingleProductRequestDto
    {
        public ProductTypeEnum ProductType { get; set; } = ProductTypeEnum.Normal;

        public string? ProductCode { get; set; }
        public string ItemNumber { get; set; } = string.Empty;
        public string? Barcode { get; set; }
        public string ChineseName { get; set; } = string.Empty;
        public string? EnglishName { get; set; }
        public decimal? DomesticPrice { get; set; }
        public decimal OEMPrice { get; set; }
        public decimal ImportPrice { get; set; }
        public decimal? Volume { get; set; }
        public int? CategoryId { get; set; }
        public bool IsLocalSupplier { get; set; }
        public int? SupplierId { get; set; }
        /// <summary>国内供应商编码（如 "200"），货号为空时用于自动生成，优先于 SupplierId</summary>
        public string? SupplierCode { get; set; }
        public int? PackSize { get; set; }
        public bool IsActive { get; set; } = true;
        public string? ImageUrl { get; set; }

        public SetTypeEnum? SetType { get; set; }
        public List<SetItemDto>? SetItems { get; set; }
        public List<MultiCodeItemDto>? MultiCodeItems { get; set; }
        public List<StorePriceDto>? StorePrices { get; set; }
    }

    /// <summary>
    /// 新建单个商品响应 DTO
    /// </summary>
    public class CreateSingleProductResponseDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ProductCode { get; set; }
        public string? ItemNumber { get; set; }
        /// <summary>生成的或传入的条码，自动生成时由后端返回</summary>
        public string? Barcode { get; set; }
        public bool BarcodeExists { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// 获取国内商品不在仓库的请求 DTO
    /// </summary>
    public class GetDomesticProductsNotInWarehouseRequestDto : ReactTableRequestDto
    {
        public int? SupplierId { get; set; }
        public ProductTypeEnum? ProductType { get; set; }
    }

    /// <summary>
    /// 国内商品不在仓库的 DTO
    /// </summary>
    public class DomesticProductNotInWarehouseDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public string ItemNumber { get; set; } = string.Empty;
        public string? Barcode { get; set; }
        public string? ProductImage { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? EnglishName { get; set; }
        public ProductTypeEnum ProductType { get; set; }
        public decimal? DomesticPrice { get; set; }
        public decimal OEMPrice { get; set; }
        public decimal ImportPrice { get; set; }
        public decimal? Volume { get; set; }
        public string? SupplierName { get; set; }
        public int? SupplierId { get; set; }
        public bool HasSetProducts { get; set; }
        public bool HasMultiCodes { get; set; }
        public List<SetItemDto>? SetItems { get; set; }
        public List<MultiCodeItemDto>? MultiCodeItems { get; set; }
    }

    /// <summary>
    /// 从国内商品导入请求 DTO
    /// </summary>
    public class ImportFromDomesticRequestDto
    {
        public List<string> ProductCodes { get; set; } = new List<string>();
        public bool SyncStorePrices { get; set; } = false;
        public bool SyncMultiCodes { get; set; } = false;
        public Dictionary<string, ImportPriceOverrideDto>? PriceOverrides { get; set; }
    }

    /// <summary>
    /// 价格覆盖 DTO
    /// </summary>
    public class ImportPriceOverrideDto
    {
        public decimal? DomesticPrice { get; set; }
        public decimal? OEMPrice { get; set; }
        public decimal? ImportPrice { get; set; }
        public decimal? Volume { get; set; }
    }

    /// <summary>
    /// 导入结果详情 DTO
    /// </summary>
    public class ImportResultDetailDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public string ItemNumber { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 从国内商品导入响应 DTO
    /// </summary>
    public class ImportFromDomesticResponseDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public List<ImportResultDetailDto> Results { get; set; } = new List<ImportResultDetailDto>();
    }

    /// <summary>
    /// 条码对应套装价/进货价明细项（商品类型≠0 时前端编辑，保存后同步 ProductSetCode / StoreMultiCodeProduct）
    /// </summary>
    public class BarcodePriceItemDto
    {
        /// <summary>条码（对应 ProductSetCode.SetBarcode 或 StoreMultiCodeProduct.MultiBarcode）</summary>
        public string Barcode { get; set; } = string.Empty;
        /// <summary>套装/多码零售价</summary>
        public decimal? RetailPrice { get; set; }
        /// <summary>套装/多码进货价</summary>
        public decimal? PurchasePrice { get; set; }
        /// <summary>可选：套装主键 SetCodeId，用于精确更新 ProductSetCode</summary>
        public string? SetCodeId { get; set; }
        /// <summary>可选：多码主键 UUID，用于精确更新 StoreMultiCodeProduct</summary>
        public string? MultiCodeUuid { get; set; }
    }

    /// <summary>
    /// 仓库商品完整更新请求 DTO（六表 + 国内商品联动，含商品类型≠0 时条码价明细）
    /// </summary>
    public class WarehouseProductFullUpdateDto
    {
        // ---------- 主表通用（Product / DomesticProduct / WarehouseProduct）----------
        public string? ProductName { get; set; }
        public string? EnglishName { get; set; }
        public string? ProductSpecification { get; set; }
        public string? Material { get; set; }
        public string? Remark { get; set; }
        public int? PackingQuantity { get; set; }
        public decimal? UnitVolume { get; set; }
        public decimal? GrossWeight { get; set; }
        public string? PackingSize { get; set; }
        public decimal? DomesticPrice { get; set; }
        public decimal? OEMPrice { get; set; }
        public decimal? ImportPrice { get; set; }
        public bool IsActive { get; set; } = true;
        public string? ProductImage { get; set; }
        public int ProductType { get; set; } = 0;
        public int? MiddlePackQuantity { get; set; }

        /// <summary>
        /// 是否自动定价（对应 Product.IsAutoPricing）
        /// </summary>
        public bool IsAutoPricing { get; set; } = false;

        // ---------- 分类与供应商 ----------
        public string? WarehouseCategoryGUID { get; set; }
        public string? SupplierCode { get; set; }
        public string? LocalSupplierCode { get; set; }

        // ---------- 仓库商品 ----------
        public int? MinOrderQuantity { get; set; }

        // ---------- 商品类型≠0 时：条码→套装价/进货价明细（前端编辑后提交，同步 ProductSetCode / StoreMultiCodeProduct）----------
        public List<BarcodePriceItemDto>? BarcodePrices { get; set; }
    }

    /// <summary>
    /// 仓库商品完整更新结果
    /// </summary>
    public class WarehouseProductFullUpdateResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 仓库商品批量上下架请求
    /// </summary>
    public class BatchToggleWarehouseProductsActiveRequestDto
    {
        public List<string> ProductCodes { get; set; } = new List<string>();
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// 仓库商品批量上下架结果
    /// </summary>
    public class BatchToggleWarehouseProductsActiveResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>
    /// 获取非 Hotbargain 商品不在仓库的请求 DTO
    /// </summary>
    public class GetNonHotbargainProductsNotInWarehouseRequestDto : ReactTableRequestDto
    {
    }

    /// <summary>
    /// 非 Hotbargain 商品不在仓库的 DTO
    /// </summary>
    public class NonHotbargainProductNotInWarehouseDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public string ItemNumber { get; set; } = string.Empty;
        public string? Barcode { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? EnglishName { get; set; }
        public ProductTypeEnum ProductType { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? RetailPrice { get; set; }
        public string? LocalSupplierCode { get; set; }
        public string? LocalSupplierName { get; set; }
        public string? ProductImage { get; set; }
    }

    /// <summary>
    /// 导入非 Hotbargain 商品请求 DTO
    /// </summary>
    public class ImportNonHotbargainRequestDto
    {
        public List<string> ProductCodes { get; set; } = new List<string>();
    }

    /// <summary>
    /// 仓库商品 HQ 同步后台任务状态常量。
    /// </summary>
    public static class WarehouseProductHqSyncJobStatusConstants
    {
        public const string Running = "Running";
        public const string Succeeded = "Succeeded";
        public const string Failed = "Failed";
    }

    /// <summary>
    /// 创建仓库商品 HQ 同步后台任务请求。
    /// </summary>
    public class WarehouseProductHqSyncJobRequestDto
    {
        public string OperationId { get; set; } = string.Empty;
    }

    /// <summary>
    /// 仓库商品 HQ 同步后台任务状态。
    /// </summary>
    public class WarehouseProductHqSyncJobDto
    {
        public string JobId { get; set; } = string.Empty;
        public string OperationId { get; set; } = string.Empty;
        public string Status { get; set; } = WarehouseProductHqSyncJobStatusConstants.Running;
        public bool IsDuplicateRequest { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? Message { get; set; }
        public SyncResult? Result { get; set; }
    }

    public class WarehouseMobileProductDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public string? ProductImage { get; set; }
        public int? ProductType { get; set; }
        public string? ProductTypeLabel { get; set; }
        public string? LocalSupplierCode { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierName { get; set; }
        public string? Grade { get; set; }
        /// <summary>
        /// 仓库商品上下架状态，移动端优先读取该字段。
        /// </summary>
        public bool WarehouseIsActive { get; set; }
        /// <summary>
        /// 兼容旧移动端字段，始终返回与 WarehouseIsActive 相同的值。
        /// </summary>
        public bool IsActive { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? RetailPrice { get; set; }
        public decimal? DomesticPrice { get; set; }
        public decimal? OEMPrice { get; set; }
        public decimal? ImportPrice { get; set; }
        public int? StockQuantity { get; set; }
        public int? MiddlePackageQuantity { get; set; }
        public int? PackingQuantity { get; set; }
        public decimal? Volume { get; set; }
        public string? LocationGuid { get; set; }
        public string? LocationCode { get; set; }
        public string? LocationBarcode { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class WarehouseMobileProductPatchDto
    {
        /// <summary>
        /// 仓库商品上下架状态，移动端新字段。
        /// </summary>
        public bool? WarehouseIsActive { get; set; }
        /// <summary>
        /// 兼容旧移动端字段，服务层会回退读取该值。
        /// </summary>
        public bool? IsActive { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? RetailPrice { get; set; }
        /// <summary>
        /// 零售价保存时是否同步所有启用分店的零售价。
        /// </summary>
        public bool? SyncStoreRetailPrices { get; set; }
        public decimal? DomesticPrice { get; set; }
        public decimal? OEMPrice { get; set; }
        public decimal? ImportPrice { get; set; }
        public int? StockQuantity { get; set; }
        public int? MiddlePackageQuantity { get; set; }
        public int? PackingQuantity { get; set; }
        public decimal? Volume { get; set; }
        public string? Grade { get; set; }
        public string? ProductImage { get; set; }
    }

    public class SetWarehouseProductLocationDto
    {
        public string? LocationGuid { get; set; }
    }

    public class WarehouseProductLabelPrintDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public string? SupplierName { get; set; }
        public decimal? RetailPrice { get; set; }
        public decimal? DomesticPrice { get; set; }
        public decimal? OEMPrice { get; set; }
        public decimal? ImportPrice { get; set; }
        public int? MiddlePackageQuantity { get; set; }
        public string? LocationCode { get; set; }
        public string? LocationBarcode { get; set; }
    }

    public class WarehouseLocationLabelPrintDto
    {
        public string LocationGuid { get; set; } = string.Empty;
        public string? LocationCode { get; set; }
        public string? LocationBarcode { get; set; }
        public string? ItemNumber { get; set; }
        public string? ProductName { get; set; }
        public int? MiddlePackageQuantity { get; set; }
        public int ProductCount { get; set; }
    }
}
