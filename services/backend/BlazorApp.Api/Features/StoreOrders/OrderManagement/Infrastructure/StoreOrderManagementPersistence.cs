using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.OrderManagement.Domain;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.OrderManagement.Infrastructure;

internal sealed class StoreOrderManagementPersistence(
    SqlSugarContext context,
    IStoreOrderActorContext actorContext,
    IStoreOrderProductCostCoordinator productCostCoordinator
)
{
    private readonly ISqlSugarClient _db = context.Db;

    internal async Task<WareHouseOrder?> GetEditableOrderAsync(string orderGuid)
    {
        var order = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID == orderGuid && !item.IsDeleted)
            .FirstAsync();

        return order != null && StoreOrderManagementRules.IsEditableOrder(order)
            ? order
            : null;
    }

    internal async Task<WareHouseOrder?> GetOrderAsync(string orderGuid)
    {
        return await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID == orderGuid && !item.IsDeleted)
            .FirstAsync();
    }

    internal async Task AddOrUpdateDetailAsync(
        WareHouseOrder order,
        string productCode,
        decimal quantity,
        decimal? importPrice,
        bool isUpdate,
        bool isBatch = false,
        decimal? originalQuantity = null
    )
    {
        var now = DateTime.Now;
        var currentUser = actorContext.ActorName;
        var warehouseProduct = await _db.Queryable<WarehouseProduct>()
            .Where(item => item.ProductCode == productCode)
            .FirstAsync();

        if (warehouseProduct == null)
        {
            var product = await _db.Queryable<Product>()
                .Where(item => item.ProductCode == productCode)
                .FirstAsync();
            if (product == null)
            {
                throw new Exception($"Product {productCode} not found");
            }

            warehouseProduct = new WarehouseProduct
            {
                OEMPrice = 0,
                ImportPrice = 0,
                MinOrderQuantity = 1,
            };
        }

        var minimumOrderQuantity = StoreOrderManagementRules.NormalizeMinimumOrderQuantity(
            warehouseProduct.MinOrderQuantity
        );
        if (isUpdate && (!isBatch || originalQuantity.HasValue) && quantity < 0)
        {
            throw new Exception($"商品数量 {quantity}");
        }

        var oemPrice = warehouseProduct.OEMPrice ?? 0;
        var finalImportPrice = importPrice ?? warehouseProduct.ImportPrice ?? 0;
        var detail = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item =>
                item.OrderGUID == order.OrderGUID
                && item.ProductCode == productCode
                && !item.IsDeleted
            )
            .FirstAsync();

        if (detail == null)
        {
            detail = new WareHouseOrderDetails
            {
                DetailGUID = UuidHelper.GenerateUuid7(),
                OrderGUID = order.OrderGUID,
                StoreCode = order.StoreCode,
                ProductCode = productCode,
                Quantity = 0,
                OEMPrice = oemPrice,
                OEMAmount = oemPrice * minimumOrderQuantity,
                AllocQuantity = minimumOrderQuantity,
                ImportPrice = finalImportPrice,
                ImportAmount = 0,
                IsDeleted = false,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = currentUser,
                UpdatedBy = currentUser,
            };
            await _db.Insertable(detail).ExecuteCommandAsync();
            return;
        }

        if (isUpdate)
        {
            if (!isBatch || originalQuantity.HasValue)
            {
                detail.AllocQuantity = quantity;
            }

            if (importPrice.HasValue)
            {
                detail.ImportPrice = importPrice.Value;
            }
        }
        else
        {
            detail.AllocQuantity += minimumOrderQuantity;
        }

        if (
            StoreOrderManagementRules.ShouldSoftDeleteExistingDetail(
                detail.Quantity,
                detail.AllocQuantity
            )
        )
        {
            await SoftDeleteOrderDetailAsync(detail, currentUser, now);
            return;
        }

        detail.OEMAmount = detail.AllocQuantity * detail.OEMPrice;
        detail.ImportAmount = StoreOrderManagementRules.CalculateOrderImportAmount(
            detail.Quantity,
            detail.ImportPrice
        );
        detail.UpdatedAt = now;
        detail.UpdatedBy = currentUser;
        await _db.Updateable(detail).ExecuteCommandAsync();
    }

    internal async Task UpdateOrderTotalAsync(string orderGuid)
    {
        var summary = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.OrderGUID == orderGuid && !item.IsDeleted)
            .Select(item => new StoreOrderTotalsRow
            {
                TotalQuantity = SqlFunc.AggregateSum(item.Quantity ?? 0),
                TotalSku = SqlFunc.AggregateDistinctCount(item.ProductCode),
                TotalAmount = SqlFunc.AggregateSum(item.OEMAmount ?? 0),
                TotalImportAmount = SqlFunc.AggregateSum(item.ImportAmount ?? 0),
            })
            .FirstAsync();

        var totalAmount = summary?.TotalAmount ?? 0;
        var totalImportAmount = summary?.TotalImportAmount ?? 0;
        var revisionAt = StoreOrderManagementRules.ResolveOrderRevisionAt(DateTime.Now);
        await _db.Updateable<WareHouseOrder>()
            .SetColumns(item => new WareHouseOrder
            {
                OEMTotalAmount = totalAmount,
                ImportTotalAmount = totalImportAmount,
                UpdatedAt = revisionAt,
            })
            .Where(item => item.OrderGUID == orderGuid)
            .ExecuteCommandAsync();
    }

    internal async Task SyncImportPriceToProductTablesAsync(
        string productCode,
        decimal importPrice
    )
    {
        var normalizedProductCode = productCode.Trim();
        if (string.IsNullOrWhiteSpace(normalizedProductCode) || importPrice <= 0)
        {
            return;
        }

        var now = DateTime.Now;
        var currentUser = actorContext.ActorName;
        var product = await productCostCoordinator
            .WithUpdateLock(
                _db.Queryable<Product>()
                    .Where(item =>
                        item.ProductCode == normalizedProductCode && !item.IsDeleted
                    )
            )
            .FirstAsync();
        if (product == null)
        {
            throw new Exception($"Product {normalizedProductCode} not found");
        }

        await _db.Updateable<Product>()
            .SetColumns(item => item.PurchasePrice == importPrice)
            .SetColumns(item => item.UpdatedAt == now)
            .SetColumns(item => item.UpdatedBy == currentUser)
            .Where(item => item.ProductCode == normalizedProductCode && !item.IsDeleted)
            .ExecuteCommandAsync();

        var warehouseProduct = await productCostCoordinator
            .WithUpdateLock(
                _db.Queryable<WarehouseProduct>()
                    .Where(item =>
                        item.ProductCode == normalizedProductCode && !item.IsDeleted
                    )
            )
            .FirstAsync();
        if (warehouseProduct == null)
        {
            warehouseProduct = new WarehouseProduct
            {
                ProductCode = normalizedProductCode,
                OEMPrice = 0,
                ImportPrice = importPrice,
                MinOrderQuantity = 1,
                IsActive = product.IsActive,
                CreatedAt = now,
                CreatedBy = currentUser,
                UpdatedAt = now,
                UpdatedBy = currentUser,
                IsDeleted = false,
            };
            await _db.Insertable(warehouseProduct).ExecuteCommandAsync();
        }
        else
        {
            await _db.Updateable<WarehouseProduct>()
                .SetColumns(item => item.ImportPrice == importPrice)
                .SetColumns(item => item.UpdatedAt == now)
                .SetColumns(item => item.UpdatedBy == currentUser)
                .Where(item =>
                    item.ProductCode == normalizedProductCode && !item.IsDeleted
                )
                .ExecuteCommandAsync();
        }

        product = await productCostCoordinator
            .WithUpdateLock(
                _db.Queryable<Product>()
                    .Where(item =>
                        item.ProductCode == normalizedProductCode && !item.IsDeleted
                    )
            )
            .FirstAsync();
        if (product == null)
        {
            throw new Exception($"Product {normalizedProductCode} not found after update");
        }

        await UpsertActiveStoreRetailPurchasePricesAsync(
            product,
            normalizedProductCode,
            importPrice,
            now,
            currentUser
        );
    }

    private async Task UpsertActiveStoreRetailPurchasePricesAsync(
        Product product,
        string productCode,
        decimal importPrice,
        DateTime now,
        string currentUser
    )
    {
        var activeStoreCodes = (await _db.Queryable<Store>()
                .Where(store => store.IsActive && !store.IsDeleted)
                .Select(store => store.StoreCode)
                .ToListAsync())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (activeStoreCodes.Count == 0)
        {
            return;
        }

        var existingPrices = await _db.Queryable<StoreRetailPrice>()
            .Where(price =>
                price.ProductCode == productCode
                && price.StoreCode != null
                && activeStoreCodes.Contains(price.StoreCode)
                && !price.IsDeleted
            )
            .ToListAsync();
        var existingStoreCodes = existingPrices
            .Select(price => price.StoreCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var price in existingPrices)
        {
            price.PurchasePrice = importPrice;
            price.UpdatedAt = now;
            price.UpdatedBy = currentUser;
        }

        if (existingPrices.Count > 0)
        {
            await _db.Updateable(existingPrices)
                .UpdateColumns(price => new
                {
                    price.PurchasePrice,
                    price.UpdatedAt,
                    price.UpdatedBy,
                })
                .ExecuteCommandAsync();
        }

        var pricesToInsert = activeStoreCodes
            .Where(storeCode => !existingStoreCodes.Contains(storeCode))
            .Select(storeCode => new StoreRetailPrice
            {
                UUID = UuidHelper.GenerateUuid7(),
                StoreCode = storeCode,
                ProductCode = productCode,
                StoreProductCode = storeCode + productCode,
                SupplierCode = product.LocalSupplierCode,
                PurchasePrice = importPrice,
                StoreRetailPriceValue = product.RetailPrice,
                DiscountRate = null,
                IsActive = product.IsActive,
                IsAutoPricing = product.IsAutoPricing,
                IsSpecialProduct = product.IsSpecialProduct,
                CreatedAt = now,
                CreatedBy = currentUser,
                UpdatedAt = now,
                UpdatedBy = currentUser,
                IsDeleted = false,
            })
            .ToList();

        if (pricesToInsert.Count > 0)
        {
            await _db.Insertable(pricesToInsert).ExecuteCommandAsync();
        }
    }

    private async Task SoftDeleteOrderDetailAsync(
        WareHouseOrderDetails detail,
        string currentUser,
        DateTime now
    )
    {
        detail.IsDeleted = true;
        detail.UpdatedAt = now;
        detail.UpdatedBy = currentUser;
        await _db.Updateable(detail).ExecuteCommandAsync();
    }

    private sealed class StoreOrderTotalsRow
    {
        public decimal TotalQuantity { get; set; }

        public int TotalSku { get; set; }

        public decimal TotalAmount { get; set; }

        public decimal TotalImportAmount { get; set; }
    }
}
