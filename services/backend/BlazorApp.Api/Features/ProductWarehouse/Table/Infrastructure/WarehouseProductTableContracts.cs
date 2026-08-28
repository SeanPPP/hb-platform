using System;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.ProductWarehouse;

internal sealed record WarehouseProductTableQueryOutcome(
    ReactTableResponseDto<WarehouseProductReactListDto> Response,
    ProductWarehouseTableRequestSnapshot Request,
    ProductWarehouseTableTimingSnapshot Timings,
    int Total,
    int ItemCount
);

internal sealed class WarehouseProductTableLocationRow
{
    public string ProductCode { get; set; } = string.Empty;
    public string? LocationCode { get; set; }
    public string? LocationBarcode { get; set; }
}

internal sealed class WarehouseProductTableRow
{
    public string ProductCode { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string? EnglishName { get; set; }
    public string? ItemNumber { get; set; }
    public string? Barcode { get; set; }
    public string? CategoryName { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierCode { get; set; }
    public string? DomesticSupplierName { get; set; }
    public string? DomesticSupplierCode { get; set; }
    public string? LocalSupplierCode { get; set; }
    public string? LocalSupplierName { get; set; }
    public decimal? DomesticPrice { get; set; }
    public decimal? OEMPrice { get; set; }
    public decimal? ImportPrice { get; set; }
    public decimal? WarehouseVolume { get; set; }
    public decimal? DomesticUnitVolume { get; set; }
    public int? DomesticPackingQuantity { get; set; }
    public int? WarehousePackingQuantity { get; set; }
    public int? MinOrderQuantity { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public string? ProductImage { get; set; }
    public int ProductType { get; set; }
}
