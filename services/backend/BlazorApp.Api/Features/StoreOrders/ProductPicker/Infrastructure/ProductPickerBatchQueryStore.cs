using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Domain;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.ProductPicker.Infrastructure;

internal sealed class ProductPickerBatchQueryStore(SqlSugarContext context)
{
    private readonly ISqlSugarClient _db = context.Db;

    internal async Task<List<StoreOrderProductDto>> LookupAsync(
        ProductPickerBatchLookupInput input
    )
    {
        var codes = input.Codes.ToList();
        return await _db.Queryable<Product>()
            .InnerJoin<WarehouseProduct>(
                (product, warehouseProduct) =>
                    product.ProductCode == warehouseProduct.ProductCode
            )
            .LeftJoin<WarehouseCategory>(
                (product, warehouseProduct, category) =>
                    product.WarehouseCategoryGUID == category.CategoryGUID
            )
            .LeftJoin<ProductGrade>(
                (product, warehouseProduct, category, grade) =>
                    product.ProductCode == grade.ProductCode && !grade.IsDeleted
            )
            .Where(
                (product, warehouseProduct, category, grade) =>
                    !product.IsDeleted && !warehouseProduct.IsDeleted
            )
            .Where(
                (product, warehouseProduct, category, grade) =>
                    (product.ItemNumber != null && codes.Contains(product.ItemNumber))
                    || (product.Barcode != null && codes.Contains(product.Barcode))
                    || (
                        product.ProductCode != null
                        && codes.Contains(product.ProductCode)
                    )
            )
            .Select(
                (product, warehouseProduct, category, grade) =>
                    new StoreOrderProductDto
                    {
                        ProductCode = product.ProductCode ?? string.Empty,
                        ItemNumber = product.ItemNumber,
                        Barcode = product.Barcode,
                        ProductName = product.ProductName,
                        ProductImage = product.ProductImage,
                        CategoryName = category.CategoryName,
                        WarehouseCategoryGUID = product.WarehouseCategoryGUID,
                        OEMPrice = warehouseProduct.OEMPrice,
                        MinOrderQuantity = warehouseProduct.MinOrderQuantity ?? 1,
                        StockQuantity = warehouseProduct.StockQuantity ?? 0,
                        PackQty = product.MiddlePackageQuantity,
                        ImportPrice = warehouseProduct.ImportPrice,
                        Grade = grade.Grade,
                    }
            )
            .ToListAsync();
    }
}
