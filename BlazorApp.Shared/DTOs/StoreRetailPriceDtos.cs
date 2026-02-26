using System;
using System.Collections.Generic;

namespace BlazorApp.Shared.DTOs
{
    public class BatchUpdateSpecialRequestDto
    {
        public List<string> ProductCodes { get; set; } = new List<string>();
        public bool IsSpecial { get; set; }
    }
    public class StoreRetailPriceListDto
    {
        public string UUID { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
        public string? StoreName { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierName { get; set; }
        public string? ProductCode { get; set; }
        public string? ProductName { get; set; }
        public string? ProductImage { get; set; }
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? StoreRetailPriceValue { get; set; }
        public decimal? DiscountRate { get; set; }
        public bool IsActive { get; set; }
        public bool IsAutoPricing { get; set; }
        public bool? IsSpecialProduct { get; set; }
        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class StoreRetailPriceDetailDto
    {
        public string UUID { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
        public string? StoreName { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierName { get; set; }
        public string? ProductCode { get; set; }
        public string? ProductName { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? StoreRetailPriceValue { get; set; }
        public decimal? DiscountRate { get; set; }
        public bool IsActive { get; set; }
        public bool IsAutoPricing { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class CreateStoreRetailPriceDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string SupplierCode { get; set; } = string.Empty;
        public decimal? PurchasePrice { get; set; }
        public decimal? StoreRetailPriceValue { get; set; }
        public decimal? DiscountRate { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsAutoPricing { get; set; }
    }

    public class UpdateStoreRetailPriceDto
    {
        public decimal? PurchasePrice { get; set; }
        public decimal? StoreRetailPriceValue { get; set; }
        public decimal? DiscountRate { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsAutoPricing { get; set; }
    }

    public class StoreRetailPriceUpsertItemDto
    {
        public string? UUID { get; set; }
        public string StoreCode { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string SupplierCode { get; set; } = string.Empty;
        public decimal? PurchasePrice { get; set; }
        public decimal? StoreRetailPriceValue { get; set; }
        public decimal? DiscountRate { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsAutoPricing { get; set; }
    }

    public class BatchResultDto
    {
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class StoreRetailPriceUpsertForActiveStoresItemDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public decimal? PurchasePrice { get; set; }
        public decimal? StoreRetailPriceValue { get; set; }
        public decimal? DiscountRate { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsAutoPricing { get; set; }
    }
}
