using System.Collections.Generic;

namespace BlazorApp.Shared.DTOs
{
    public class ProductSetCodeGridDto
    {
        public string SetCodeId { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        /// <summary>
        /// 多码商品编码，对应 StoreMultiCodeProduct.MultiCodeProductCode，用于同步分店多码表
        /// </summary>
        public string? SetProductCode { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierName { get; set; }
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public string SetItemNumber { get; set; } = string.Empty;
        public string? SetBarcode { get; set; }
        public decimal? SetPurchasePrice { get; set; }
        public decimal? SetRetailPrice { get; set; }
        public bool IsActive { get; set; }
        public string? UpdatedBy { get; set; }
        public System.DateTime? UpdatedAt { get; set; }
    }

    public class BatchUpdateStatusDto
    {
        public List<string> Ids { get; set; } = new();
        public bool IsActive { get; set; }
    }

    public class BatchUpdatePricesItemDto
    {
        public string Id { get; set; } = string.Empty;
        public decimal? SetPurchasePrice { get; set; }
        public decimal? SetRetailPrice { get; set; }
    }

    public class BatchUpdatePricesDto
    {
        public List<BatchUpdatePricesItemDto> Items { get; set; } = new();
    }

    public class BatchDeleteSetCodesRequestDto
    {
        public List<string> Ids { get; set; } = new();
    }

    public class BatchUpdateBarcodesItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string? SetBarcode { get; set; }
    }

    public class BatchUpdateBarcodesDto
    {
        public List<BatchUpdateBarcodesItemDto> Items { get; set; } = new();
    }

    public class CreateSetCodeItemDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public string? SetItemNumber { get; set; }
        public string? SetBarcode { get; set; }
        public decimal? SetPurchasePrice { get; set; }
        public decimal? SetRetailPrice { get; set; }
        public bool? IsActive { get; set; }
    }

    public class BatchCreateSetCodesDto
    {
        public List<CreateSetCodeItemDto> Items { get; set; } = new();
    }

    public class CreateSetCodeWithStoreSyncDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public string? SetItemNumber { get; set; }
        public string? SetBarcode { get; set; }
        public decimal? SetPurchasePrice { get; set; }
        public decimal? SetRetailPrice { get; set; }
        public bool? IsActive { get; set; }
        public List<string> StoreCodes { get; set; } = new();
        public string SupplierCode { get; set; } = "200";
    }

    public class BatchCreateSetCodeWithStoreSyncDto
    {
        public List<CreateSetCodeWithStoreSyncDto> Items { get; set; } = new();
    }

    public class BatchDeleteSetCodeWithStoreSyncDto
    {
        public List<string> Ids { get; set; } = new();
        public List<string> StoreCodes { get; set; } = new();
    }
}
