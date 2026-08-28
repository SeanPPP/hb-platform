using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.ProductWarehouse;

/// <summary>
/// 批量更新请求到仓库、国内商品及 HQ 图片结果的集中映射。
/// </summary>
internal static class WarehouseProductBatchUpdateEntityMapper
{
    internal static WarehouseProduct CreateWarehouseProduct(
        string productCode,
        UpdateItemDto item,
        string updatedBy
    ) =>
        new()
        {
            ProductCode = productCode,
            DomesticPrice = item.DomesticPrice,
            OEMPrice = item.OEMPrice,
            ImportPrice = item.ImportPrice,
            Volume = item.Volume,
            PackingQuantity = item.PackingQuantity,
            MinOrderQuantity = item.MinOrderQuantity,
            StockQuantity = 0,
            IsActive = item.IsActive ?? true,
            IsDeleted = false,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            CreatedBy = updatedBy,
            UpdatedBy = updatedBy,
        };

    internal static void ApplyWarehouseProductUpdate(
        WarehouseProduct warehouseProduct,
        UpdateItemDto item,
        string updatedBy
    )
    {
        if (item.DomesticPrice.HasValue)
            warehouseProduct.DomesticPrice = item.DomesticPrice;
        if (item.OEMPrice.HasValue)
            warehouseProduct.OEMPrice = item.OEMPrice;
        if (item.ImportPrice.HasValue)
            warehouseProduct.ImportPrice = item.ImportPrice;
        if (item.Volume.HasValue)
            warehouseProduct.Volume = item.Volume;
        if (item.PackingQuantity.HasValue)
            warehouseProduct.PackingQuantity = item.PackingQuantity.Value;
        if (item.MinOrderQuantity.HasValue)
            warehouseProduct.MinOrderQuantity = item.MinOrderQuantity.Value;
        if (item.IsActive.HasValue)
            warehouseProduct.IsActive = item.IsActive.Value;

        warehouseProduct.UpdatedAt = DateTime.Now;
        warehouseProduct.UpdatedBy = updatedBy;
    }

    internal static DomesticProduct CreateDomesticProduct(
        string productCode,
        string updatedBy,
        DateTime now
    ) =>
        new()
        {
            ProductCode = productCode,
            CreatedAt = now,
            CreatedBy = updatedBy,
        };

    internal static void RefreshDomesticProduct(
        DomesticProduct domesticProduct,
        Product product,
        WarehouseProduct warehouseProduct
    )
    {
        domesticProduct.ProductName = product.ProductName;
        domesticProduct.EnglishProductName = product.EnglishName;
        domesticProduct.HBProductNo = product.ItemNumber;
        domesticProduct.Barcode = product.Barcode;
        domesticProduct.ProductType = product.ProductType ?? 0;
        domesticProduct.DomesticPrice = warehouseProduct.DomesticPrice;
        domesticProduct.OEMPrice = warehouseProduct.OEMPrice ?? product.RetailPrice;
        domesticProduct.ImportPrice = warehouseProduct.ImportPrice ?? product.PurchasePrice;
        domesticProduct.PackingQuantity = warehouseProduct.PackingQuantity;
        domesticProduct.UnitVolume = warehouseProduct.Volume;
        domesticProduct.MiddlePackQuantity = warehouseProduct.MinOrderQuantity;
        domesticProduct.ProductImage = product.ProductImage;
        domesticProduct.IsActive = warehouseProduct.IsActive;
        domesticProduct.IsDeleted = false;
    }

    internal static void ApplyDomesticSupplier(
        DomesticProduct domesticProduct,
        string supplierCode,
        string updatedBy,
        DateTime now
    )
    {
        domesticProduct.SupplierCode = supplierCode;
        domesticProduct.UpdatedAt = now;
        domesticProduct.UpdatedBy = updatedBy;
    }

    internal static List<ProductHqImageUpdateItemDto> MapImageUpdates(
        IReadOnlyDictionary<string, string> imageUrlByCode
    ) =>
        imageUrlByCode
            .Select(pair => new ProductHqImageUpdateItemDto
            {
                ProductCode = pair.Key,
                ImageUrl = pair.Value,
            })
            .ToList();
}
