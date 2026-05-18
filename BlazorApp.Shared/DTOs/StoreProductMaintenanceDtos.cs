namespace BlazorApp.Shared.DTOs
{
    public class StoreProductLookupRequestDto
    {
        public string Keyword { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
    }

    public class StoreProductLookupItemDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public string? ProductImage { get; set; }
        public string? MatchSource { get; set; }
        public string? MatchValue { get; set; }
        public string? ProductTypeLabel { get; set; }
        public string? Grade { get; set; }
    }

    public class StoreProductDetailDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public string? ProductImage { get; set; }
        public int? ProductType { get; set; }
        public string? ProductTypeLabel { get; set; }
        public string? Grade { get; set; }
        public string? LocalSupplierCode { get; set; }
        public string? LocalSupplierName { get; set; }
        public StoreProductStorePriceDto? StorePrice { get; set; }
        public StoreProductClearancePriceDto? ClearancePrice { get; set; }
        public List<StoreProductSetCodeDto> SetCodes { get; set; } = new();
        public List<StoreProductMultiCodeDto> MultiCodes { get; set; } = new();
    }

    public class StoreProductStorePriceDto
    {
        public string Uuid { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
        public string? StoreName { get; set; }
        public string? ProductCode { get; set; }
        public string? StoreProductCode { get; set; }
        public string? SupplierCode { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? RetailPrice { get; set; }
        public decimal? DiscountRate { get; set; }
        public bool IsAutoPricing { get; set; }
        public bool IsSpecialProduct { get; set; }
        public bool IsActive { get; set; }
        public decimal? Rate { get; set; }
        public string? StrategySourceLabel { get; set; }
        public string? StrategyRuleLabel { get; set; }
    }

    public class StoreProductMultiCodeDto
    {
        public string Uuid { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
        public string? ProductCode { get; set; }
        public string? MultiCodeProductCode { get; set; }
        public string? StoreMultiCodeProductCode { get; set; }
        public string? Barcode { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? RetailPrice { get; set; }
        public decimal? DiscountRate { get; set; }
        public bool IsAutoPricing { get; set; }
        public bool IsSpecialProduct { get; set; }
        public bool IsActive { get; set; }
        public decimal? Rate { get; set; }
        public string? StrategySourceLabel { get; set; }
        public string? StrategyRuleLabel { get; set; }
    }

    public class StoreProductClearancePriceDto
    {
        public string Uuid { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
        public string? StoreName { get; set; }
        public string? ProductCode { get; set; }
        public string? ClearanceBarcode { get; set; }
        public decimal? ClearancePrice { get; set; }
    }

    public class StoreProductSetCodeDto
    {
        public string SetCodeId { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string SetProductCode { get; set; } = string.Empty;
        public string SetItemNumber { get; set; } = string.Empty;
        public string? SetBarcode { get; set; }
        public decimal? SetPurchasePrice { get; set; }
        public decimal? SetRetailPrice { get; set; }
        public int SetQuantity { get; set; }
        public int SetType { get; set; }
        public string? SetTypeDescription { get; set; }
        public bool IsActive { get; set; }
    }

    public class UpdateStoreProductPriceDto
    {
        public decimal? PurchasePrice { get; set; }
        public decimal? RetailPrice { get; set; }
        public bool? IsAutoPricing { get; set; }
        public bool? IsSpecialProduct { get; set; }
        public bool? IsActive { get; set; }
    }

    public class UpdateStoreProductMultiCodeDto
    {
        public decimal? PurchasePrice { get; set; }
        public decimal? RetailPrice { get; set; }
        public bool? IsAutoPricing { get; set; }
        public bool? IsSpecialProduct { get; set; }
        public bool? IsActive { get; set; }
    }
}
