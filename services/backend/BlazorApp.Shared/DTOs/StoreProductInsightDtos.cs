namespace BlazorApp.Shared.DTOs;

/// <summary>移动端单店商品进销查询的日期范围。</summary>
public sealed class ProductInsightRangeDto
{
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
}

public sealed class ProductInsightStoreDto
{
    public string StoreCode { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
}

public sealed class ProductInsightProductDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? ItemNumber { get; set; }
    public string? Barcode { get; set; }
    public string? ProductImage { get; set; }
    public string? LocalSupplierCode { get; set; }
    public string? LocalSupplierName { get; set; }
}

public sealed class ProductInsightMovementDto
{
    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? SupplierName { get; set; }
}

public sealed class ProductInsightOrderDto
{
    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal DeliveredQuantity { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class ProductInsightDailySalesDto
{
    public DateTime Date { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
}

public sealed class ProductInsightSalesDto
{
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public List<ProductInsightDailySalesDto> Records { get; set; } = [];
}

public sealed class ProductInsightPurchasesDto
{
    public decimal Quantity { get; set; }
    public int DocumentCount { get; set; }
    public List<ProductInsightMovementDto> Records { get; set; } = [];
    public ProductInsightMovementDto? LastRecord { get; set; }
}

public sealed class ProductInsightWarehouseDto
{
    public decimal OrderedQuantity { get; set; }
    public decimal DeliveredQuantity { get; set; }
    public List<ProductInsightOrderDto> Orders { get; set; } = [];
    public List<ProductInsightMovementDto> Deliveries { get; set; } = [];
    public ProductInsightMovementDto? LastDelivery { get; set; }
}

/// <summary>GET /api/react/v1/product-insights/store 的数据载荷。</summary>
public sealed class StoreProductInsightDto
{
    public ProductInsightRangeDto Range { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
    public DateTime? SalesStatisticLastUpdatedAt { get; set; }
    public ProductInsightStoreDto Store { get; set; } = new();
    public ProductInsightProductDto Product { get; set; } = new();
    public string SourceType { get; set; } = "local";
    public ProductInsightSalesDto Sales { get; set; } = new();
    public ProductInsightPurchasesDto Purchases { get; set; } = new();
    public ProductInsightWarehouseDto Warehouse { get; set; } = new();
}
