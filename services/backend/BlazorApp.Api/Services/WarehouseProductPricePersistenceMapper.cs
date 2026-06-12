using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services
{
    public static class WarehouseProductPricePersistenceMapper
    {
        public static void ApplyUpdate(
            UpdateWarehouseProductDto dto,
            WarehouseProduct warehouseProduct,
            Product product,
            IEnumerable<StoreRetailPrice> storeRetailPrices,
            DateTime updatedAt
        )
        {
            var purchasePrice = dto.PurchasePrice ?? dto.ImportPrice;
            var retailPrice = dto.RetailPrice ?? dto.OEMPrice;
            var packingQuantity = dto.PackingQty ?? dto.PackingQuantity;

            warehouseProduct.DomesticPrice = dto.DomesticPrice;
            warehouseProduct.ImportPrice = purchasePrice;
            warehouseProduct.OEMPrice = retailPrice;
            warehouseProduct.StockQuantity = dto.StockQuantity;
            warehouseProduct.MinOrderQuantity = dto.MinOrderQuantity;
            warehouseProduct.StockValue = dto.StockValue;
            warehouseProduct.StockAlertQuantity = dto.StockAlertQuantity;
            warehouseProduct.IsActive = dto.IsActive;
            warehouseProduct.Volume = dto.Volume;
            warehouseProduct.PackingQuantity = packingQuantity;
            warehouseProduct.UpdatedAt = updatedAt;

            if (!string.IsNullOrWhiteSpace(dto.ProductName))
            {
                product.ProductName = dto.ProductName;
            }

            if (!string.IsNullOrWhiteSpace(dto.Barcode))
            {
                product.Barcode = dto.Barcode;
            }

            product.PurchasePrice = purchasePrice;
            product.RetailPrice = retailPrice;
            product.MiddlePackageQuantity = dto.MiddlePackageQuantity;
            product.UpdatedAt = updatedAt;

            foreach (var storeRetailPrice in storeRetailPrices)
            {
                storeRetailPrice.PurchasePrice = purchasePrice;
                storeRetailPrice.StoreRetailPriceValue = retailPrice;
                storeRetailPrice.UpdatedAt = updatedAt;
            }
        }
    }
}
