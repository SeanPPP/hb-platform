using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    public class LocalSupplierInvoiceListDto
    {
        public string InvoiceGUID { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
        public string? StoreName { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierName { get; set; }
        public string? InvoiceNo { get; set; }
        public int? VoucherType { get; set; }
        public DateTime? OrderDate { get; set; }
        public DateTime? InboundDate { get; set; }
        public decimal? TotalAmount { get; set; }
        public decimal? ReceivedTotalAmount { get; set; }
        public int? FlowStatus { get; set; }
        public int? InboundStatus { get; set; }
        public string? Remarks { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class LocalSupplierInvoiceDetailDto
    {
        public string InvoiceGUID { get; set; } = string.Empty;
        public string? AppGUID { get; set; }
        public string? PcGUID { get; set; }
        public string? StoreCode { get; set; }
        public string? StoreName { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierName { get; set; }
        public string? InvoiceNo { get; set; }
        public int? VoucherType { get; set; }
        public DateTime? OrderDate { get; set; }
        public DateTime? InboundDate { get; set; }
        public decimal? TotalAmount { get; set; }
        public decimal? ReceivedTotalAmount { get; set; }
        public string? VoucherImage { get; set; }
        public string? Remarks { get; set; }
        public string? ImportTemplate { get; set; }
        public int? FlowStatus { get; set; }
        public int? InboundStatus { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class LocalSupplierInvoiceItemDto
    {
        public string DetailGUID { get; set; } = string.Empty;
        public string? InvoiceGUID { get; set; }
        public string? StoreCode { get; set; }
        public string? SupplierCode { get; set; }
        public string? ProductTagGUID { get; set; }
        public string? ProductCategoryGUID { get; set; }
        public string? StoreProductCode { get; set; }
        public string? ProductCode { get; set; }
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public string? ProductName { get; set; }
        public string? Specification { get; set; }
        public string? Unit { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? LastPurchasePrice { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? RetailPrice { get; set; }
        public decimal? Amount { get; set; }
        public int? ExistingProductCount { get; set; }
        public int? BarcodeStatus { get; set; }
        public int? BarcodeMatchCount { get; set; }
        public string? ProductImage { get; set; }
        public int? ActivityType { get; set; }
        public decimal? DiscountRate { get; set; }
        public bool? AutoPricing { get; set; }
        public decimal? PricingFloatRate { get; set; }
        public decimal? NewAutoRetailPrice { get; set; }
        public bool? IsSpecialProduct { get; set; }
        public string? OldStoreProductCode { get; set; }
    }

    public class CreateInvoiceRequest
    {
        public string StoreCode { get; set; } = string.Empty;
        public string SupplierCode { get; set; } = string.Empty;
        public string InvoiceNo { get; set; } = string.Empty;
        public DateTime? OrderDate { get; set; }
        public DateTime? InboundDate { get; set; }
        public string? Remarks { get; set; }
        public List<PastedDetailItem> Items { get; set; } = new();
    }

    public class PastedDetailItem
    {
        public string? ItemNumber { get; set; }
        public string? NameOrBarcode { get; set; }
        public string? ProductName { get; set; }
        public string? Barcode { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string? StoreProductCode { get; set; }
        public string? ProductCode { get; set; }
        public decimal? LastPurchasePrice { get; set; }
        public decimal? RetailPrice { get; set; }
        public bool? AutoPricing { get; set; }
        public decimal? PricingFloatRate { get; set; }
        public decimal? NewAutoRetailPrice { get; set; }
        public bool? IsSpecialProduct { get; set; }
    }

    public class DetectSupplierItemRequest
    {
        public string StoreCode { get; set; } = string.Empty;
        public string SupplierCode { get; set; } = string.Empty;
        public List<DetectSupplierItem> Items { get; set; } = new();
    }

    public class DetectSupplierItem
    {
        public string? ItemNumber { get; set; }
    }

    public class SupplierItemDetectResult
    {
        public bool Exists { get; set; }
        public string? ProductCode { get; set; }
        public string? StoreProductCode { get; set; }
        public string? ProductName { get; set; }
        public string? ProductImage { get; set; }
        public decimal? CurrentPurchasePrice { get; set; }
        public decimal? CurrentRetailPrice { get; set; }
        public string? Error { get; set; }
    }

    public class DetectBarcodeRequest
    {
        public string StoreCode { get; set; } = string.Empty;
        public List<DetectBarcodeItem> Items { get; set; } = new();
    }

    public class DetectBarcodeItem
    {
        public string? Barcode { get; set; }
    }

    public class BarcodeDetectResult
    {
        public bool Matched { get; set; }
        public int MatchCount { get; set; }
        public bool OverTwo { get; set; }
        public List<string>? ProductCodes { get; set; }
        public List<string>? StoreProductCodes { get; set; }
        public List<string>? ProductNames { get; set; }
        public string? FirstProductImage { get; set; }
        public string? Error { get; set; }
    }

    public class UpdateInvoiceRequest
    {
        public string? StoreCode { get; set; }
        public string? SupplierCode { get; set; }
        public string? InvoiceNo { get; set; }
        public DateTime? OrderDate { get; set; }
        public DateTime? InboundDate { get; set; }
        public string? Remarks { get; set; }
        public string? VoucherImage { get; set; }
        public int? FlowStatus { get; set; }
        public int? InboundStatus { get; set; }
    }

    public class InvoiceDetailUpsertItemDto
    {
        public string? DetailGUID { get; set; }
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public string? ProductName { get; set; }
        public string? ProductCategoryGUID { get; set; }
        public string? StoreProductCode { get; set; }
        public string? ProductCode { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? LastPurchasePrice { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? RetailPrice { get; set; }
        public decimal? Amount { get; set; }
        public int? ActivityType { get; set; }
        public decimal? DiscountRate { get; set; }
        public bool? AutoPricing { get; set; }
        public decimal? PricingFloatRate { get; set; }
        public decimal? NewAutoRetailPrice { get; set; }
        public bool? IsSpecialProduct { get; set; }
    }

    /// <summary>
    /// 更新到分店价格请求DTO
    /// </summary>
    public class UpdateToStorePricesRequest
    {
        /// <summary>
        /// 订单GUID
        /// </summary>
        [Required]
        public string InvoiceGuid { get; set; } = string.Empty;

        /// <summary>
        /// 要更新的明细GUID列表
        /// </summary>
        [Required]
        public List<string> DetailGuids { get; set; } = new();

        /// <summary>
        /// 目标分店代码列表
        /// </summary>
        [Required]
        public List<string> TargetStoreCodes { get; set; } = new();

        /// <summary>
        /// 要更新的字段配置
        /// </summary>
        [Required]
        public UpdateToStorePricesFields UpdateFields { get; set; } = new();

        /// <summary>
        /// 是否在更新本地分店价格后同步商品和价格到HQ
        /// </summary>
        public bool UpdateHqProduct { get; set; }
    }

    /// <summary>
    /// 更新到分店价格结果DTO
    /// </summary>
    public class UpdateToStorePricesResultDto : BatchResultDto
    {
        public int HqExisting { get; set; }
        public int HbwebCreated { get; set; }
        public int HqCreated { get; set; }
        public int HqSynced { get; set; }
        public int HqPurchasePricesUpdated { get; set; }
        public int HqFailed { get; set; }
        public List<EnsureHqProductError> HqErrors { get; set; } = new();
    }

    /// <summary>
    /// 同步本地进货单明细商品到HQ请求DTO
    /// </summary>
    public class EnsureHqProductsRequest
    {
        /// <summary>
        /// 要同步的明细GUID列表
        /// </summary>
        public List<string> DetailGuids { get; set; } = new();

        /// <summary>
        /// 更新已有商品时只同步这些目标分店
        /// </summary>
        public List<string> TargetStoreCodes { get; set; } = new();

        /// <summary>
        /// 幂等键，当前后端保留字段以兼容前端请求
        /// </summary>
        public string? IdempotencyKey { get; set; }
    }

    /// <summary>
    /// 同步本地进货单明细商品到HQ结果DTO
    /// </summary>
    public class EnsureHqProductsResult
    {
        public int Total { get; set; }
        public int HqExisting { get; set; }
        public int HbwebCreated { get; set; }
        public int HqCreated { get; set; }
        public int HqSynced { get; set; }
        public int HqPurchasePricesUpdated { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public List<EnsureHqProductError> Errors { get; set; } = new();
    }

    /// <summary>
    /// 同步HQ商品逐行错误
    /// </summary>
    public class EnsureHqProductError
    {
        public string DetailGuid { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 更新到分店价格字段配置DTO
    /// </summary>
    public class UpdateToStorePricesFields
    {
        /// <summary>
        /// 是否更新进价
        /// </summary>
        public bool UpdatePurchasePrice { get; set; }

        /// <summary>
        /// 进价值（当UpdatePurchasePrice为true时使用）
        /// </summary>
        public decimal? PurchasePrice { get; set; }

        /// <summary>
        /// 是否更新零售价
        /// </summary>
        public bool UpdateRetailPrice { get; set; }

        /// <summary>
        /// 零售价值（当UpdateRetailPrice为true时使用）
        /// </summary>
        public decimal? RetailPrice { get; set; }

        /// <summary>
        /// 是否更新自动定价标志
        /// </summary>
        public bool UpdateIsAutoPricing { get; set; }

        /// <summary>
        /// 自动定价标志值（当UpdateIsAutoPricing为true时使用）
        /// </summary>
        public bool? IsAutoPricing { get; set; }

        /// <summary>
        /// 是否更新特价产品标志
        /// </summary>
        public bool UpdateIsSpecialProduct { get; set; }

        /// <summary>
        /// 特价产品标志值（当UpdateIsSpecialProduct为true时使用）
        /// </summary>
        public bool? IsSpecialProduct { get; set; }

        /// <summary>
        /// 是否更新折扣率
        /// </summary>
        public bool UpdateDiscountRate { get; set; }

        /// <summary>
        /// 折扣率值（当UpdateDiscountRate为true时使用）
        /// </summary>
        public decimal? DiscountRate { get; set; }
    }

    /// <summary>
    /// 检测商品请求DTO
    /// </summary>
    public class CheckProductsRequest
    {
        /// <summary>
        /// 订单GUID
        /// </summary>
        public string InvoiceGuid { get; set; } = string.Empty;

        /// <summary>
        /// 要检测的明细GUID列表（为空则检测所有）
        /// </summary>
        public List<string>? DetailGuids { get; set; }
    }

    /// <summary>
    /// 检测商品结果DTO
    /// </summary>
    public class ProductCheckResultDto
    {
        /// <summary>
        /// 明细GUID
        /// </summary>
        public string DetailGuid { get; set; } = string.Empty;

        /// <summary>
        /// 商品状态：0=未检测，1=已存在，2=不存在
        /// </summary>
        public int ProductStatus { get; set; }

        /// <summary>
        /// 条码状态：0=未检测，1=正常，2=异常
        /// </summary>
        public int BarcodeStatus { get; set; }

        /// <summary>
        /// 商品存在数量
        /// </summary>
        public int ExistingProductCount { get; set; }

        /// <summary>
        /// 自动定价
        /// </summary>
        public bool? AutoPricing { get; set; }

        /// <summary>
        /// 是否特殊商品
        /// </summary>
        public bool? IsSpecialProduct { get; set; }

        /// <summary>
        /// 折扣率
        /// </summary>
        public decimal? DiscountRate { get; set; }

        /// <summary>
        /// 分店商品编码
        /// </summary>
        public string? StoreProductCode { get; set; }

        /// <summary>
        /// 上次进货价
        /// </summary>
        public decimal? LastPurchasePrice { get; set; }

        /// <summary>
        /// 定价浮动率
        /// </summary>
        public decimal? PricingFloatRate { get; set; }

        /// <summary>
        /// 新自动零售价
        /// </summary>
        public decimal? NewAutoRetailPrice { get; set; }

        /// <summary>
        /// 商品信息
        /// </summary>
        public ProductCheckInfoDto? ProductInfo { get; set; }

        /// <summary>
        /// 条码匹配数量
        /// </summary>
        public int BarcodeMatchCount { get; set; }

        /// <summary>
        /// 默认操作类型
        /// </summary>
        public int DefaultAction { get; set; }
    }

    /// <summary>
    /// 商品信息DTO（用于检测）
    /// </summary>
    public class ProductCheckInfoDto
    {
        public string? ProductCode { get; set; }
        public string? ProductName { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? RetailPrice { get; set; }
        public string? ProductImage { get; set; }
        public string? StoreProductCode { get; set; }
    }

    /// <summary>
    /// 检测商品响应DTO
    /// </summary>
    public class CheckProductsResponseDto
    {
        /// <summary>
        /// 检测结果列表
        /// </summary>
        public List<ProductCheckResultDto> Results { get; set; } = new();

        /// <summary>
        /// 汇总信息
        /// </summary>
        public CheckProductsSummaryDto Summary { get; set; } = new();
    }

    /// <summary>
    /// 检测商品汇总DTO
    /// </summary>
    public class CheckProductsSummaryDto
    {
        public int Total { get; set; }
        public int ProductExists { get; set; }
        public int ProductNotExists { get; set; }
        public int BarcodeNormal { get; set; }
        public int BarcodeAbnormal { get; set; }
    }

    /// <summary>
    /// 粘贴数据请求DTO
    /// </summary>
    public class PasteDetailsRequest
    {
        /// <summary>
        /// 订单GUID
        /// </summary>
        public string InvoiceGuid { get; set; } = string.Empty;

        /// <summary>
        /// 模式：append=追加，replace=覆盖
        /// </summary>
        public string Mode { get; set; } = "replace";

        /// <summary>
        /// 要粘贴的明细列表
        /// </summary>
        public List<PastedDetailItemDto> Items { get; set; } = new();
    }

    /// <summary>
    /// 粘贴的明细项DTO
    /// </summary>
    public class PastedDetailItemDto
    {
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public string? ProductName { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? NewAutoRetailPrice { get; set; }
        /// <summary>
        /// 零售价（如果有值，则写入并关闭自动定价）
        /// </summary>
        public decimal? RetailPrice { get; set; }
    }

    /// <summary>
    /// 更新明细操作类型请求DTO
    /// </summary>
    public class UpdateDetailActionRequest
    {
        /// <summary>
        /// 操作类型：0=无操作，1=新建商品，2=更新进货价，3=等待操作，4=更新货号，5=添加多码
        /// 99=已执行完成，仅服务端内部写入，客户端不可提交
        /// </summary>
        public int Action { get; set; }
    }

    /// <summary>
    /// 批量更新明细操作类型请求DTO
    /// </summary>
    public class BatchUpdateDetailActionRequest
    {
        /// <summary>
        /// 明细GUID列表
        /// </summary>
        public List<string> DetailGuids { get; set; } = new();

        /// <summary>
        /// 操作类型：0=无操作，1=新建商品，2=更新进货价，3=等待操作，4=更新货号，5=添加多码
        /// 99=已执行完成，仅服务端内部写入，客户端不可提交
        /// </summary>
        public int Action { get; set; }
    }

    /// <summary>
    /// 条码异常匹配商品DTO
    /// </summary>
    public class BarcodeAbnormalMatchedProductDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string SupplierCode { get; set; } = string.Empty;
        public string? SupplierName { get; set; }
        public string? ItemNumber { get; set; }
        public string Barcode { get; set; } = string.Empty;
        public string? ProductImage { get; set; }
        public bool IsMultiCode { get; set; }
        public bool IsBundle { get; set; }
        public int? ProductType { get; set; }
    }

    /// <summary>
    /// 条码异常明细DTO
    /// </summary>
    public class BarcodeAbnormalDetailDto
    {
        public string DetailGuid { get; set; } = string.Empty;
        public string ItemNumber { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int ProductStatus { get; set; }
        public string? MatchedProductCode { get; set; }
        public List<BarcodeAbnormalMatchedProductDto> MatchedProducts { get; set; } = new();
    }

    /// <summary>
    /// 获取条码异常明细响应DTO
    /// </summary>
    public class GetBarcodeAbnormalDetailsResponse
    {
        public List<BarcodeAbnormalDetailDto> Details { get; set; } = new();
    }

    /// <summary>
    /// 按条码查询匹配商品响应DTO
    /// </summary>
    public class GetProductsByBarcodeResponse
    {
        public string Barcode { get; set; } = string.Empty;
        public List<BarcodeAbnormalMatchedProductDto> MatchedProducts { get; set; } = new();
    }

    /// <summary>
    /// 按商品编码查询匹配商品响应DTO
    /// </summary>
    public class GetProductsByProductCodeResponse
    {
        public string ProductCode { get; set; } = string.Empty;
        public List<BarcodeAbnormalMatchedProductDto> MatchedProducts { get; set; } = new();
    }

    /// <summary>
    /// 检查随货单号是否存在请求DTO
    /// </summary>
    public class CheckInvoiceNoExistsRequest
    {
        public string StoreCode { get; set; } = string.Empty;
        public string SupplierCode { get; set; } = string.Empty;
        public string InvoiceNo { get; set; } = string.Empty;
    }

    /// <summary>
    /// 检查随货单号是否存在响应DTO
    /// </summary>
    public class InvoiceNoCheckResult
    {
        public bool Exists { get; set; }
        public string? ExistingInvoiceNo { get; set; }
        public DateTime? ExistingCreatedAt { get; set; }
    }

    /// <summary>
    /// 明细操作类型枚举
    /// </summary>
    public enum DetailAction
    {
        /// <summary>无操作</summary>
        None = 0,

        /// <summary>新建商品</summary>
        CreateProduct = 1,

        /// <summary>更新进货价</summary>
        UpdatePurchasePrice = 2,

        /// <summary>等待操作</summary>
        WaitForOperation = 3,

        /// <summary>更新货号</summary>
        UpdateItemNumber = 4,

        /// <summary>添加多码</summary>
        AddMultiCode = 5,
    }

    /// <summary>
    /// 批量执行操作请求DTO
    /// </summary>
    public class BatchExecuteActionsRequestDto
    {
        /// <summary>
        /// 进货单GUID
        /// </summary>
        public string InvoiceGuid { get; set; } = string.Empty;

        /// <summary>
        /// 要执行的明细GUID列表
        /// </summary>
        public List<string> DetailGuids { get; set; } = new();
    }

    /// <summary>
    /// 批量执行操作结果DTO
    /// </summary>
    public class BatchExecuteActionsResultDto
    {
        /// <summary>
        /// 创建商品成功数
        /// </summary>
        public int CreatedProducts { get; set; }

        /// <summary>
        /// 更新进货价成功数
        /// </summary>
        public int UpdatedPurchasePrices { get; set; }

        /// <summary>
        /// 更新货号成功数
        /// </summary>
        public int UpdatedItemNumbers { get; set; }

        /// <summary>
        /// 添加多码成功数
        /// </summary>
        public int AddedMultiCodes { get; set; }

        /// <summary>
        /// 跳过数（无操作/等待操作）
        /// </summary>
        public int Skipped { get; set; }

        /// <summary>
        /// 失败数
        /// </summary>
        public int Failed { get; set; }

        /// <summary>
        /// 错误信息列表
        /// </summary>
        public List<string> Errors { get; set; } = new();
    }

    public class PushToHqRequest
    {
        public List<string> InvoiceGuids { get; set; } = new();
    }

    public class LocalSupplierInvoiceHqSyncRequest
    {
        public List<string>? SelectedStoreCodes { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    public class LocalSupplierInvoiceHqSyncResult
    {
        public string RequestId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public long DurationMs { get; set; }
        public int InvoiceAddedCount { get; set; }
        public int InvoiceUpdatedCount { get; set; }
        public int DetailAddedCount { get; set; }
        public int DetailUpdatedCount { get; set; }
        public int TotalProcessed { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
