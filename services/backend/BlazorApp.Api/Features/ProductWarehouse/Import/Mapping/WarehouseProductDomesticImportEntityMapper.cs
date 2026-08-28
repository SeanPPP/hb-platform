using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.ProductWarehouse;

/// <summary>
/// 国内导入来源到仓库、商品、套装与门店实体的集中映射。
/// </summary>
internal static class WarehouseProductDomesticImportEntityMapper
{
    internal static WarehouseProduct CreateWarehouseProduct(
        string productCode,
        decimal? domesticPrice,
        decimal? retailPrice,
        decimal? importPrice,
        decimal? volume,
        string updatedBy,
        DateTime now
    ) =>
        new()
        {
            ProductCode = productCode,
            DomesticPrice = domesticPrice,
            OEMPrice = retailPrice,
            ImportPrice = importPrice,
            Volume = volume,
            StockQuantity = 0,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = updatedBy,
            UpdatedBy = updatedBy,
        };

    internal static void ApplyWarehouseProductUpdate(
        WarehouseProduct warehouseProduct,
        decimal? domesticPrice,
        decimal? retailPrice,
        decimal? importPrice,
        decimal? volume,
        string updatedBy,
        DateTime now
    )
    {
        warehouseProduct.DomesticPrice = domesticPrice;
        warehouseProduct.OEMPrice = retailPrice;
        warehouseProduct.ImportPrice = importPrice;
        warehouseProduct.Volume = volume;
        warehouseProduct.UpdatedAt = now;
        warehouseProduct.UpdatedBy = updatedBy;
    }

    internal static void ApplyDomesticProductUpdate(
        DomesticProduct domesticProduct,
        decimal? domesticPrice,
        decimal? retailPrice,
        decimal? importPrice,
        decimal? volume,
        string? imageUrl,
        bool wasTranslated,
        string? resolvedEnglishName,
        string updatedBy,
        DateTime now
    )
    {
        domesticProduct.DomesticPrice = domesticPrice;
        domesticProduct.OEMPrice = retailPrice;
        domesticProduct.ImportPrice = importPrice;
        domesticProduct.UnitVolume = volume;
        domesticProduct.ProductImage = imageUrl;
        if (
            wasTranslated
            && !string.IsNullOrWhiteSpace(resolvedEnglishName)
        )
        {
            domesticProduct.EnglishProductName = resolvedEnglishName;
        }
        domesticProduct.UpdatedAt = now;
        domesticProduct.UpdatedBy = updatedBy;
    }

    internal static Product CreateProduct(
        DomesticProduct domesticProduct,
        WarehouseProduct warehouseProduct,
        string displayName,
        string? resolvedEnglishName,
        string updatedBy,
        DateTime now
    ) =>
        new()
        {
            ProductCode = domesticProduct.ProductCode,
            ItemNumber = domesticProduct.HBProductNo,
            Barcode = domesticProduct.Barcode,
            LocalSupplierCode = "200",
            ProductType = domesticProduct.ProductType,
            ProductName = displayName,
            EnglishName = resolvedEnglishName,
            PurchasePrice = warehouseProduct.ImportPrice,
            RetailPrice = warehouseProduct.OEMPrice,
            ProductImage = ProductImageUrlHelper.EnsureImageUrl(
                domesticProduct.ProductImage,
                domesticProduct.HBProductNo ?? domesticProduct.ProductCode
            ),
            IsAutoPricing = false,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = updatedBy,
            UpdatedBy = updatedBy,
        };

    internal static void ApplySmartFilledProductName(
        Product product,
        string translatedEnglishName,
        string updatedBy,
        DateTime now
    )
    {
        product.ProductName = translatedEnglishName;
        product.EnglishName = translatedEnglishName;
        product.UpdatedAt = now;
        product.UpdatedBy = updatedBy;
    }

    internal static ProductSetCode CreateProductSetCode(
        string productCode,
        DomesticSetProduct source,
        decimal? warehouseRetailPrice,
        DateTime now
    ) =>
        new()
        {
            SetCodeId = source.SetProductCode,
            ProductCode = productCode,
            SetProductCode = source.SetProductCode,
            SetItemNumber = source.SetProductNo,
            SetBarcode = source.SetBarcode,
            // Type1/Type2 关系先落库；成本在所有门店投影完成后锁内统一派生。
            SetPurchasePrice = null,
            SetRetailPrice = source.OEMPrice ?? warehouseRetailPrice,
            SetQuantity = 1,
            SetType = 1,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

    internal static StoreMultiCodeProduct CreateStoreMultiCodeProduct(
        string productCode,
        string storeCode,
        DomesticSetProduct source,
        DateTime now
    ) =>
        new()
        {
            UUID = UuidHelper.GenerateUuid7(),
            ProductCode = productCode,
            StoreCode = storeCode,
            MultiCodeProductCode = source.SetProductCode,
            StoreMultiCodeProductCode = storeCode + source.SetProductCode,
            MultiBarcode = source.SetBarcode,
            PurchasePrice = null,
            MultiCodeRetailPrice = source.OEMPrice,
            DiscountRate = 0,
            IsAutoPricing = false,
            IsSpecialProduct = false,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

    internal static StoreRetailPrice CreateStoreRetailPrice(
        string productCode,
        string storeCode,
        WarehouseProduct warehouseProduct,
        DateTime now
    ) =>
        new()
        {
            ProductCode = productCode,
            StoreCode = storeCode,
            StoreProductCode = storeCode + productCode,
            SupplierCode = "200",
            PurchasePrice = warehouseProduct.ImportPrice,
            StoreRetailPriceValue = warehouseProduct.OEMPrice,
            DiscountRate = 0,
            IsAutoPricing = false,
            IsSpecialProduct = false,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

    internal static WarehouseProduct CreateNonHotbargainWarehouseProduct(
        string productCode,
        Product product,
        string updatedBy,
        DateTime now
    ) =>
        new()
        {
            ProductCode = productCode,
            DomesticPrice = 0,
            OEMPrice = 0,
            ImportPrice = product.PurchasePrice ?? 0,
            StockQuantity = 0,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = updatedBy,
            UpdatedBy = updatedBy,
        };

    internal static void RestoreNonHotbargainWarehouseProduct(
        WarehouseProduct warehouseProduct,
        Product product,
        string updatedBy,
        DateTime now
    )
    {
        warehouseProduct.DomesticPrice = 0;
        warehouseProduct.OEMPrice = 0;
        warehouseProduct.ImportPrice = product.PurchasePrice ?? 0;
        warehouseProduct.StockQuantity = 0;
        warehouseProduct.MinOrderQuantity = null;
        warehouseProduct.StockValue = null;
        warehouseProduct.StockAlertQuantity = null;
        warehouseProduct.Volume = null;
        warehouseProduct.PackingQuantity = null;
        warehouseProduct.IsActive = true;
        warehouseProduct.IsDeleted = false;
        warehouseProduct.UpdatedAt = now;
        warehouseProduct.UpdatedBy = updatedBy;
    }
}
