namespace BlazorApp.Shared.DTOs;

/// <summary>
/// 商品维护 outbox 的完整最新投影。队列合并时以最新 payload 为准，执行时仍会回读服务端已提交状态。
/// </summary>
public sealed class ProductMaintenanceHqProjectionPayloadDto
{
    public ProductMaintenanceHqProductProjectionDto? Product { get; init; }

    public List<ProductMaintenanceHqStorePriceProjectionDto> StorePrices { get; init; } = new();

    public List<ProductMaintenanceHqSetCodeProjectionDto> ProductSetCodes { get; init; } = new();

    public List<ProductMaintenanceHqStoreMultiCodeProjectionDto> StoreMultiCodes { get; init; } = new();

    public List<ProductMaintenanceHqClearancePriceProjectionDto> ClearancePrices { get; init; } = new();
}

public sealed class ProductMaintenanceHqProductProjectionDto
{
    public string ProductCode { get; init; } = string.Empty;
    public string? SupplierCode { get; init; }
    public string? ItemNumber { get; init; }
    public string? Barcode { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? EnglishName { get; init; }
    public int? ProductType { get; init; }
    public decimal? PurchasePrice { get; init; }
    public decimal? RetailPrice { get; init; }
    public bool IsAutoPricing { get; init; }
    public bool IsSpecialProduct { get; init; }
    public bool IsActive { get; init; }
}

public sealed class ProductMaintenanceHqStorePriceProjectionDto
{
    public string StoreCode { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string? StoreProductCode { get; init; }
    public string? SupplierCode { get; init; }
    public decimal? PurchasePrice { get; init; }
    public decimal? RetailPrice { get; init; }
    public decimal? DiscountRate { get; init; }
    public bool IsAutoPricing { get; init; }
    public bool IsSpecialProduct { get; init; }
    public bool IsActive { get; init; }
}

public sealed class ProductMaintenanceHqSetCodeProjectionDto
{
    public string SetCodeId { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string SetProductCode { get; init; } = string.Empty;
    public string SetItemNumber { get; init; } = string.Empty;
    public string? SetBarcode { get; init; }
    public decimal? PurchasePrice { get; init; }
    public decimal? RetailPrice { get; init; }
    public int Quantity { get; init; }
    public int Type { get; init; }
    public bool IsActive { get; init; }
}

public sealed class ProductMaintenanceHqStoreMultiCodeProjectionDto
{
    public string StoreCode { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string MultiCodeProductCode { get; init; } = string.Empty;
    public string? StoreMultiCodeProductCode { get; init; }
    public string? Barcode { get; init; }
    public decimal? PurchasePrice { get; init; }
    public decimal? RetailPrice { get; init; }
    public decimal? DiscountRate { get; init; }
    public bool IsAutoPricing { get; init; }
    public bool IsSpecialProduct { get; init; }
    public bool IsActive { get; init; }
}

public sealed class ProductMaintenanceHqClearancePriceProjectionDto
{
    public string StoreCode { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string? Barcode { get; init; }
    public decimal? Price { get; init; }
}
