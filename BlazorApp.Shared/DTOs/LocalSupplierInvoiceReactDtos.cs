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
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
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
}
