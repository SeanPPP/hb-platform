using System;
using System.Collections.Generic;

namespace BlazorApp.Shared.DTOs
{
    public class StoreMultiCodePriceListDto
    {
        public string UUID { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
        public string? StoreName { get; set; }
        public string? ProductCode { get; set; }
        public string? ProductName { get; set; }
        public string? ProductImage { get; set; }
        public string? ItemNumber { get; set; }
        public string? MultiBarcode { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? MultiCodeRetailPrice { get; set; }
        public decimal? DiscountRate { get; set; }
        public bool IsActive { get; set; }
        public bool IsAutoPricing { get; set; }
        public bool? IsSpecialProduct { get; set; }
        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class StoreMultiCodePriceUpsertItemDto
    {
        public string? UUID { get; set; }
        public string? StoreCode { get; set; }
        public string? ProductCode { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? MultiCodeRetailPrice { get; set; }
        public decimal? DiscountRate { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsAutoPricing { get; set; }
    }

    public class BatchResultDtoMC
    {
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class BatchUpdateSpecialRequestDtoMC
    {
        public List<string> ProductCodes { get; set; } = new List<string>();
        public bool IsSpecial { get; set; }
    }

    public class StoreMultiCodePriceUpsertForActiveStoresItemDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public decimal? PurchasePrice { get; set; }
        public decimal? MultiCodeRetailPrice { get; set; }
        public decimal? DiscountRate { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsAutoPricing { get; set; }
    }
}
