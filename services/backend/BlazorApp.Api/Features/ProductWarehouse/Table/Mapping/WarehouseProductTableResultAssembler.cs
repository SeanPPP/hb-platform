using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;

namespace BlazorApp.Api.Features.ProductWarehouse;

/// <summary>
/// DTO 离开查询层前统一补全图片 URL，避免把自定义方法翻译进 SQL。
/// </summary>
internal static class WarehouseProductTableResultAssembler
{
    internal static ReactTableResponseDto<WarehouseProductReactListDto> Assemble(
        IReadOnlyList<string> pageProductCodes,
        IReadOnlyList<WarehouseProductTableLocationRow> locations,
        IReadOnlyList<WarehouseProductTableRow> rows,
        int total
    )
    {
        var pageOrderMap = pageProductCodes
            .Select((code, index) => new { code, index })
            .ToDictionary(item => item.code, item => item.index);

        // 货位是一对多关系，分页后再聚合，避免主查询 count/page 被货位行数放大。
        var pickingLocationMap = locations
            .GroupBy(item => item.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Codes = group
                        .Select(item => item.LocationCode)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value)
                        .ToList(),
                    Barcodes = group
                        .Select(item => item.LocationBarcode)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value)
                        .ToList(),
                },
                StringComparer.OrdinalIgnoreCase
            );

        var items = rows
            .OrderBy(row =>
                pageOrderMap.TryGetValue(row.ProductCode, out var order) ? order : int.MaxValue
            )
            .Select(row => new WarehouseProductReactListDto
            {
                ProductCode = row.ProductCode,
                ProductName = row.ProductName,
                EnglishName = row.EnglishName,
                ItemNumber = row.ItemNumber,
                Barcode = row.Barcode,
                CategoryName = row.CategoryName,
                SupplierName = row.SupplierName,
                SupplierCode = row.SupplierCode,
                DomesticSupplierName = row.DomesticSupplierName,
                DomesticSupplierCode = row.DomesticSupplierCode,
                LocalSupplierCode = row.LocalSupplierCode,
                LocalSupplierName = row.LocalSupplierName,
                DomesticPrice = row.DomesticPrice,
                OEMPrice = row.OEMPrice,
                ImportPrice = row.ImportPrice,
                Volume = row.WarehouseVolume ?? row.DomesticUnitVolume,
                IsVolumeFallback = !row.WarehouseVolume.HasValue
                    && row.DomesticUnitVolume.HasValue,
                PackingQuantity = row.DomesticPackingQuantity ?? row.WarehousePackingQuantity,
                IsPackingQuantityFallback = !row.DomesticPackingQuantity.HasValue
                    && row.WarehousePackingQuantity.HasValue,
                MinOrderQuantity = row.MinOrderQuantity,
                IsActive = row.IsActive,
                CreatedAt = row.CreatedAt,
                UpdatedAt = row.UpdatedAt,
                // 历史记录没有更新人时不能借用创建人，前端会按空值显示“--”。
                UpdatedBy = string.IsNullOrWhiteSpace(row.UpdatedBy) ? null : row.UpdatedBy,
                ProductImage = row.ProductImage,
                ProductType = row.ProductType,
                LocationCodes = pickingLocationMap.TryGetValue(
                    row.ProductCode,
                    out var pickingLocation
                )
                    ? pickingLocation.Codes
                    : new List<string>(),
                LocationBarcodes = pickingLocationMap.TryGetValue(
                    row.ProductCode,
                    out var pickingLocationForBarcode
                )
                    ? pickingLocationForBarcode.Barcodes
                    : new List<string>(),
            })
            .ToList();

        NormalizeImageUrls(items);
        return new ReactTableResponseDto<WarehouseProductReactListDto>
        {
            Items = items,
            Total = total,
        };
    }

    internal static ReactTableResponseDto<WarehouseProductReactListDto> Empty(int total) =>
        new() { Items = new List<WarehouseProductReactListDto>(), Total = total };

    private static void NormalizeImageUrls(IEnumerable<WarehouseProductReactListDto> items)
    {
        foreach (var dto in items)
        {
            dto.ProductImage = ProductImageUrlHelper.EnsureImageUrl(
                dto.ProductImage,
                dto.ItemNumber ?? dto.ProductCode
            );
        }
    }
}
