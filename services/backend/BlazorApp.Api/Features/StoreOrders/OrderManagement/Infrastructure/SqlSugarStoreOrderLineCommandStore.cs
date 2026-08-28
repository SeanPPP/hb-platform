using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.OrderManagement.Domain;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.OrderManagement.Infrastructure;

internal sealed class SqlSugarStoreOrderLineCommandStore(
    SqlSugarContext context,
    StoreOrderTransactionExecutor transactionExecutor,
    StoreOrderManagementPersistence persistence,
    IStoreOrderActorContext actorContext,
    IStoreOrderProductCostCoordinator productCostCoordinator,
    IWarehouseProductChangeHistoryService changeHistoryService
) : IStoreOrderLineCommandStore
{
    private readonly ISqlSugarClient _db = context.Db;

    public Task<StoreOrderManagementResult<bool>> AddOrderLineAsync(
        AddOrderLineInput input
    )
    {
        return transactionExecutor.ExecuteAsync(async () =>
        {
            var order = await persistence.GetEditableOrderAsync(input.OrderGuid);
            if (order == null)
            {
                return StoreOrderManagementResult<bool>.Fail(
                    "Order not found or not editable"
                );
            }

            // 单笔新增沿用旧契约：请求中的 ImportPrice 不参与写入。
            await persistence.AddOrUpdateDetailAsync(
                order,
                input.ProductCode,
                input.Quantity,
                importPrice: null,
                isUpdate: false
            );
            await persistence.UpdateOrderTotalAsync(order.OrderGUID);
            return StoreOrderManagementResult<bool>.Ok(true);
        });
    }

    public Task<StoreOrderManagementResult<bool>> BatchAddOrderLineAsync(
        BatchAddOrderLineInput input
    )
    {
        return transactionExecutor.ExecuteAsync(async () =>
        {
            var order = await persistence.GetEditableOrderAsync(input.OrderGuid);
            if (order == null)
            {
                return StoreOrderManagementResult<bool>.Fail(
                    "Order not found or not editable"
                );
            }

            foreach (var item in input.Items)
            {
                await persistence.AddOrUpdateDetailAsync(
                    order,
                    item.ProductCode,
                    item.Quantity,
                    item.ImportPrice,
                    isUpdate: false
                );
            }

            await persistence.UpdateOrderTotalAsync(order.OrderGUID);
            return StoreOrderManagementResult<bool>.Ok(true);
        });
    }

    public Task<StoreOrderManagementResult<bool>> UpdateOrderLineAsync(
        UpdateOrderLineInput input
    )
    {
        return transactionExecutor.ExecuteAsync(async () =>
        {
            var order = await persistence.GetEditableOrderAsync(input.OrderGuid);
            if (order == null)
            {
                return StoreOrderManagementResult<bool>.Fail(
                    "Order not found or not editable"
                );
            }

            var syncImportPrice = input.SyncImportPrice && input.ImportPrice.HasValue;
            var batchGuid = Guid.NewGuid();
            StoreOrderProductCostMutationScope? costScope = null;
            IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>? beforeSnapshots = null;
            if (syncImportPrice)
            {
                costScope = await productCostCoordinator.AcquireProductsAsync(
                    new[] { input.ProductCode }
                );
                beforeSnapshots = await changeHistoryService.CaptureSnapshotsAsync(
                    new[] { input.ProductCode }
                );
            }

            await persistence.AddOrUpdateDetailAsync(
                order,
                input.ProductCode,
                input.Quantity,
                input.ImportPrice,
                isUpdate: true
            );

            if (syncImportPrice)
            {
                await persistence.SyncImportPriceToProductTablesAsync(
                    input.ProductCode,
                    input.ImportPrice!.Value
                );
                await productCostCoordinator.RecalculateAsync(
                    costScope!,
                    actorContext.ActorName
                );
            }

            await persistence.UpdateOrderTotalAsync(order.OrderGUID);
            if (syncImportPrice)
            {
                var afterSnapshots = await changeHistoryService.CaptureSnapshotsAsync(
                    new[] { input.ProductCode }
                );
                await changeHistoryService.RecordChangesAsync(
                    beforeSnapshots!,
                    afterSnapshots,
                    new WarehouseProductChangeHistoryContextDto
                    {
                        Action = "Update",
                        Source = "StoreOrderImportPriceVariance",
                        SourceReference = $"StoreOrderImportPriceVariance:{order.OrderGUID}:{input.ProductCode}",
                        BatchGuid = batchGuid,
                        ActorName = actorContext.ActorName,
                    }
                );
            }

            return StoreOrderManagementResult<bool>.Ok(true);
        });
    }

    public Task<StoreOrderManagementResult<bool>> RemoveOrderLineAsync(
        RemoveOrderLineInput input
    )
    {
        return transactionExecutor.ExecuteAsync(async () =>
        {
            var order = await persistence.GetEditableOrderAsync(input.OrderGuid);
            if (order == null)
            {
                return StoreOrderManagementResult<bool>.Fail(
                    "Order not found or not editable"
                );
            }

            var detail = await _db.Queryable<WareHouseOrderDetails>()
                .Where(item =>
                    item.OrderGUID == input.OrderGuid
                    && item.DetailGUID == input.DetailGuid
                    && !item.IsDeleted
                )
                .FirstAsync();
            if (detail == null)
            {
                return StoreOrderManagementResult<bool>.Fail("Order line not found");
            }

            detail.IsDeleted = true;
            detail.UpdatedAt = DateTime.Now;
            detail.UpdatedBy = actorContext.ActorName;
            await _db.Updateable(detail).ExecuteCommandAsync();
            await persistence.UpdateOrderTotalAsync(order.OrderGUID);
            return StoreOrderManagementResult<bool>.Ok(true);
        });
    }

    public Task<StoreOrderManagementResult<bool>> BatchUpdateOrderLineAsync(
        BatchUpdateOrderLineInput input
    )
    {
        return transactionExecutor.ExecuteAsync(async () =>
        {
            var order = await persistence.GetEditableOrderAsync(input.OrderGuid);
            if (order == null)
            {
                return StoreOrderManagementResult<bool>.Fail(
                    "Order not found or not editable"
                );
            }

            if (StoreOrderManagementRules.CanUseDetailGuidQuantityBatchUpdate(input))
            {
                return await UpdateOrderLinesByDetailGuidAsync(order, input);
            }

            var syncProductCodes = input.Items
                .Where(item => item.SyncImportPrice && item.ImportPrice.HasValue)
                .Select(item => item.ProductCode?.Trim())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var batchGuid = Guid.NewGuid();
            var costScope = await productCostCoordinator.AcquireProductsAsync(
                syncProductCodes
            );
            IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>? beforeSnapshots = null;
            if (syncProductCodes.Count > 0)
            {
                beforeSnapshots = await changeHistoryService.CaptureSnapshotsAsync(
                    syncProductCodes
                );
            }

            foreach (var item in input.Items)
            {
                await persistence.AddOrUpdateDetailAsync(
                    order,
                    item.ProductCode,
                    item.Quantity ?? 0,
                    item.ImportPrice,
                    isUpdate: true,
                    isBatch: true,
                    originalQuantity: item.Quantity
                );

                if (item.SyncImportPrice && item.ImportPrice.HasValue)
                {
                    await persistence.SyncImportPriceToProductTablesAsync(
                        item.ProductCode,
                        item.ImportPrice.Value
                    );
                }
            }

            if (syncProductCodes.Count > 0)
            {
                await productCostCoordinator.RecalculateAsync(
                    costScope,
                    actorContext.ActorName
                );
            }

            await persistence.UpdateOrderTotalAsync(order.OrderGUID);
            if (syncProductCodes.Count > 0)
            {
                var afterSnapshots = await changeHistoryService.CaptureSnapshotsAsync(
                    syncProductCodes
                );
                await changeHistoryService.RecordChangesAsync(
                    beforeSnapshots!,
                    afterSnapshots,
                    new WarehouseProductChangeHistoryContextDto
                    {
                        Action = "Update",
                        Source = "StoreOrderImportPriceVariance",
                        SourceReference = $"StoreOrderImportPriceVariance:{order.OrderGUID}:{string.Join(",", syncProductCodes)}",
                        BatchGuid = batchGuid,
                        ActorName = actorContext.ActorName,
                    }
                );
            }

            return StoreOrderManagementResult<bool>.Ok(true);
        });
    }

    public Task<StoreOrderManagementResult<RefreshOrderLineImportPricesResult>> RefreshOrderLineImportPricesAsync(
        RefreshOrderLineImportPricesInput input
    )
    {
        return transactionExecutor.ExecuteAsync(async () =>
        {
            var orderExists = await _db.Queryable<WareHouseOrder>()
                .Where(item => item.OrderGUID == input.OrderGuid && !item.IsDeleted)
                .AnyAsync();
            if (!orderExists)
            {
                return StoreOrderManagementResult<RefreshOrderLineImportPricesResult>.Fail(
                    "Order not found"
                );
            }

            var requestedDetailGuids = input.DetailGuids.ToList();
            var detailQuery = _db.Queryable<WareHouseOrderDetails>()
                .Where(item => item.OrderGUID == input.OrderGuid && !item.IsDeleted);
            if (requestedDetailGuids.Count > 0)
            {
                detailQuery = detailQuery.Where(item =>
                    requestedDetailGuids.Contains(item.DetailGUID)
                );
            }

            var details = await detailQuery.ToListAsync();
            if (details.Count == 0)
            {
                return StoreOrderManagementResult<RefreshOrderLineImportPricesResult>.Ok(
                    new RefreshOrderLineImportPricesResult(0, 0, 0, 0)
                );
            }

            var productCodes = details
                .Select(item => item.ProductCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var warehousePrices = productCodes.Count == 0
                ? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                : (await _db.Queryable<WarehouseProduct>()
                    .Where(item =>
                        productCodes.Contains(item.ProductCode) && !item.IsDeleted
                    )
                    .Select(item => new { item.ProductCode, item.ImportPrice })
                    .ToListAsync())
                .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
                .ToDictionary(
                    item => item.ProductCode,
                    item => item.ImportPrice ?? 0,
                    StringComparer.OrdinalIgnoreCase
                );

            var now = DateTime.Now;
            var unchangedCount = 0;
            var skippedCount = 0;
            var missingWarehousePriceCount = 0;
            var changedDetails = new List<WareHouseOrderDetails>();
            foreach (var detail in details)
            {
                if (
                    string.IsNullOrWhiteSpace(detail.ProductCode)
                    || !warehousePrices.TryGetValue(
                        detail.ProductCode,
                        out var warehouseImportPrice
                    )
                    || warehouseImportPrice <= 0
                )
                {
                    skippedCount += 1;
                    missingWarehousePriceCount += 1;
                    continue;
                }

                var expectedImportAmount = StoreOrderManagementRules.CalculateOrderImportAmount(
                    detail.Quantity,
                    warehouseImportPrice
                );
                var importPriceMatches =
                    detail.ImportPrice.HasValue
                    && detail.ImportPrice.Value == warehouseImportPrice;
                var importAmountMatches =
                    detail.ImportAmount.HasValue
                    && detail.ImportAmount.Value == expectedImportAmount;
                if (importPriceMatches && importAmountMatches)
                {
                    unchangedCount += 1;
                    continue;
                }

                detail.ImportPrice = warehouseImportPrice;
                detail.ImportAmount = expectedImportAmount;
                detail.UpdatedAt = now;
                detail.UpdatedBy = actorContext.ActorName;
                changedDetails.Add(detail);
            }

            if (changedDetails.Count > 0)
            {
                await _db.Updateable(changedDetails).ExecuteCommandAsync();
                await persistence.UpdateOrderTotalAsync(input.OrderGuid);
            }

            return StoreOrderManagementResult<RefreshOrderLineImportPricesResult>.Ok(
                new RefreshOrderLineImportPricesResult(
                    changedDetails.Count,
                    unchangedCount,
                    skippedCount,
                    missingWarehousePriceCount
                )
            );
        });
    }

    private async Task<StoreOrderManagementResult<bool>> UpdateOrderLinesByDetailGuidAsync(
        WareHouseOrder order,
        BatchUpdateOrderLineInput input
    )
    {
        var detailGuids = input.Items.Select(item => item.DetailGuid!.Trim()).ToList();
        if (detailGuids.Count != detailGuids.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            return StoreOrderManagementResult<bool>.Fail(
                "Duplicate order line detailGUID"
            );
        }

        if (input.Items.Any(item => item.Quantity.GetValueOrDefault() < 0))
        {
            return StoreOrderManagementResult<bool>.Fail("Quantity cannot be negative");
        }

        var details = await _db.Queryable<WareHouseOrderDetails>()
            .Where(detail =>
                detail.OrderGUID == order.OrderGUID
                && detailGuids.Contains(detail.DetailGUID)
                && !detail.IsDeleted
            )
            .ToListAsync();
        if (details.Count != detailGuids.Count)
        {
            return StoreOrderManagementResult<bool>.Fail(
                "Some order lines were not found"
            );
        }

        var now = DateTime.Now;
        var detailMap = details.ToDictionary(
            detail => detail.DetailGUID,
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var item in input.Items)
        {
            var detail = detailMap[item.DetailGuid!.Trim()];
            var allocatedQuantity = item.Quantity!.Value;
            detail.AllocQuantity = allocatedQuantity;
            detail.OEMAmount = allocatedQuantity * (detail.OEMPrice ?? 0);
            detail.ImportAmount = StoreOrderManagementRules.CalculateOrderImportAmount(
                detail.Quantity,
                detail.ImportPrice
            );
            detail.UpdatedAt = now;
            detail.UpdatedBy = actorContext.ActorName;
            if (
                StoreOrderManagementRules.ShouldSoftDelete(
                    detail.Quantity,
                    allocatedQuantity
                )
            )
            {
                detail.IsDeleted = true;
            }
        }

        await _db.Updateable(details).ExecuteCommandAsync();
        await persistence.UpdateOrderTotalAsync(order.OrderGUID);
        return StoreOrderManagementResult<bool>.Ok(true);
    }
}
