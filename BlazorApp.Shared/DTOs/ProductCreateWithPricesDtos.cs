using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    public class CreateProductWithPricesDto
    {
        [Required]
        public string ProductName { get; set; } = string.Empty;
        public string? ProductCategoryGUID { get; set; }
        public string? LocalSupplierCode { get; set; }
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? RetailPrice { get; set; }
        public bool IsAutoPricing { get; set; }
        public bool IsSpecialProduct { get; set; }
    }

    public class CreateProductWithPricesResultDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public Dictionary<string, string> StoreProductCodes { get; set; } = new Dictionary<string, string>();
    }
}
