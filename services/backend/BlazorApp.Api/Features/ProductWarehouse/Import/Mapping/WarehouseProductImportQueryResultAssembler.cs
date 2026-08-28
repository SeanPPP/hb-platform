using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.ProductWarehouse;

internal sealed record WarehouseProductImportResolvedName(
    string DisplayName,
    string? EnglishName
);

internal static class WarehouseProductImportQueryResultAssembler
{
    internal static List<DomesticProduct> CreateNameSources(
        IEnumerable<WarehouseProductDomesticImportCandidateRow> rows
    ) =>
        rows.Select(row => new DomesticProduct
            {
                ProductCode = row.ProductCode,
                HBProductNo = row.ItemNumber,
                ProductName = row.ProductName,
                EnglishProductName = row.EnglishName,
            })
            .ToList();

    internal static List<DomesticProductNotInWarehouseDto> MapDomesticCandidates(
        IEnumerable<WarehouseProductDomesticImportCandidateRow> rows,
        IReadOnlyDictionary<string, WarehouseProductImportResolvedName> names
    ) =>
        rows.Select(row =>
            {
                int? supplierId = null;
                if (
                    row.SupplierCode != null
                    && int.TryParse(row.SupplierCode, out var parsedSupplierId)
                )
                {
                    supplierId = parsedSupplierId;
                }

                return new DomesticProductNotInWarehouseDto
                {
                    ProductCode = row.ProductCode,
                    ItemNumber = row.ItemNumber ?? string.Empty,
                    Barcode = row.Barcode ?? string.Empty,
                    ProductImage = ProductImageUrlHelper.EnsureImageUrl(
                        row.ProductImage,
                        row.ItemNumber ?? string.Empty
                    ),
                    ProductName = names.TryGetValue(row.ProductCode, out var resolvedName)
                        ? resolvedName.DisplayName
                        : row.ProductName ?? string.Empty,
                    EnglishName = names.TryGetValue(row.ProductCode, out var englishName)
                        ? englishName.EnglishName
                        : row.EnglishName,
                    ProductType = row.ProductType,
                    DomesticPrice = row.DomesticPrice,
                    OEMPrice = row.OEMPrice,
                    ImportPrice = row.ImportPrice,
                    Volume = row.Volume,
                    SupplierName = row.SupplierName,
                    SupplierId = supplierId,
                };
            })
            .ToList();

    internal static void ApplyRelationFlags(
        IEnumerable<DomesticProductNotInWarehouseDto> items,
        IReadOnlyCollection<string> setProductCodes,
        IReadOnlyCollection<string?> multiCodeProductCodes
    )
    {
        foreach (var item in items)
        {
            item.HasSetProducts = setProductCodes.Contains(item.ProductCode);
            item.HasMultiCodes = multiCodeProductCodes.Contains(item.ProductCode);
        }
    }

    internal static ReactTableResponseDto<NonHotbargainProductNotInWarehouseDto> MapNonHotbargainCandidates(
        IEnumerable<WarehouseProductNonHotbargainImportCandidateRow> rows,
        int total
    ) =>
        new()
        {
            Items = rows.Select(row => new NonHotbargainProductNotInWarehouseDto
                {
                    ProductCode = row.ProductCode,
                    ItemNumber = row.ItemNumber,
                    Barcode = row.Barcode,
                    ProductName = row.ProductName!,
                    EnglishName = row.EnglishName,
                    ProductType = row.ProductType,
                    PurchasePrice = row.PurchasePrice,
                    RetailPrice = row.RetailPrice,
                    LocalSupplierCode = row.LocalSupplierCode,
                    LocalSupplierName = row.LocalSupplierName,
                    ProductImage = ProductImageUrlHelper.EnsureImageUrl(
                        row.ProductImage,
                        row.ItemNumber
                    ),
                })
                .ToList(),
            Total = total,
        };
}
