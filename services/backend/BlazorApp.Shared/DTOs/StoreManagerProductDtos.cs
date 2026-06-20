using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    public class StoreManagerProductFilterDto
    {
        public List<string>? StoreCodes { get; set; }
        public string? Search { get; set; }
        public string? SupplierName { get; set; }
        public bool? IsAutoPricing { get; set; }
        public decimal? MinPurchasePrice { get; set; }
        public decimal? MaxPurchasePrice { get; set; }
        public decimal? MinRetailPrice { get; set; }
        public decimal? MaxRetailPrice { get; set; }
        public decimal? MinDiscountRate { get; set; }
        public decimal? MaxDiscountRate { get; set; }
        public string? SortBy { get; set; }
        public string SortOrder { get; set; } = "asc";
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class StoreManagerProductListItemDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public string? ProductImage { get; set; }
    }

    public class StoreManagerStorePriceDto
    {
        public string UUID { get; set; } = string.Empty;
        public string StoreCode { get; set; } = string.Empty;
        public string StoreName { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public decimal? PurchasePrice { get; set; }
        public decimal? StoreRetailPriceValue { get; set; }
        public bool IsAutoPricing { get; set; }
    }

    public class StoreManagerMultiCodePriceDto
    {
        public string UUID { get; set; } = string.Empty;
        public string StoreCode { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string? MultiBarcode { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? MultiCodeRetailPrice { get; set; }
        public bool IsAutoPricing { get; set; }
    }

    public class StoreManagerProductDetailDto
    {
        public StoreManagerProductListItemDto Product { get; set; } = new();
        public List<StoreManagerStorePriceDto> StorePrices { get; set; } = new();
        public List<StoreManagerMultiCodePriceDto> MultiCodePrices { get; set; } = new();
    }

    public class StoreManagerUpdatePriceDto
    {
        public string UUID { get; set; } = string.Empty;
        public decimal? PurchasePrice { get; set; }
        public decimal? StoreRetailPriceValue { get; set; }
        public bool? IsAutoPricing { get; set; }
    }

    public class StoreManagerUpdateMultiCodePriceDto
    {
        public string UUID { get; set; } = string.Empty;
        public decimal? PurchasePrice { get; set; }
        public decimal? MultiCodeRetailPrice { get; set; }
        public bool? IsAutoPricing { get; set; }
    }

    public class StoreManagerPagedListDto<T>
    {
        public List<T> Items { get; set; } = new();
        public int Total { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
    }
}
