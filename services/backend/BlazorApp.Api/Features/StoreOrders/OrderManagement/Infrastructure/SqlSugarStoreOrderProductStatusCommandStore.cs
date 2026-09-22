using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.OrderManagement.Domain;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.OrderManagement.Infrastructure;

internal sealed class SqlSugarStoreOrderProductStatusCommandStore(
    SqlSugarContext context,
    StoreOrderTransactionExecutor transactionExecutor,
    IStoreOrderActorContext actorContext,
    IWarehouseProductChangeHistoryService changeHistoryService
) : IStoreOrderProductStatusCommandStore
{
    private readonly ISqlSugarClient _db = context.Db;

    public Task<StoreOrderManagementResult<bool>> UpdateProductStatusAsync(
        UpdateProductStatusInput input
    )
    {
        return transactionExecutor.ExecuteAsync(async () =>
        {
            var now = DateTime.UtcNow;
            var beforeSnapshots = await changeHistoryService.CaptureSnapshotsAsync(
                new[] { input.ProductCode }
            );
            // 订货页的上下架开关表达“仓库是否继续向分店供货”，与该页展示、筛选同源（WarehouseProduct）。
            // 不写商品主档：Product.IsActive 决定门店 POS 能否销售，由 HQ 同步维护。
            var affectedRows = await _db.Updateable<WarehouseProduct>()
                .SetColumns(warehouseProduct => new WarehouseProduct
                {
                    IsActive = input.IsActive,
                    UpdatedAt = now,
                    UpdatedBy = actorContext.ActorName,
                })
                .Where(warehouseProduct =>
                    warehouseProduct.ProductCode == input.ProductCode
                    && !warehouseProduct.IsDeleted
                )
                .ExecuteCommandAsync();
            if (affectedRows == 0)
            {
                return StoreOrderManagementResult<bool>.Fail("Product not found");
            }

            var afterSnapshots = await changeHistoryService.CaptureSnapshotsAsync(
                new[] { input.ProductCode }
            );
            await changeHistoryService.RecordChangesAsync(
                beforeSnapshots,
                afterSnapshots,
                new WarehouseProductChangeHistoryContextDto
                {
                    Action = "Update",
                    Source = "StoreOrderProductStatus",
                    SourceReference = input.ProductCode,
                    BatchGuid = Guid.NewGuid(),
                    ActorName = actorContext.ActorName,
                    OccurredAtUtc = now,
                }
            );

            return StoreOrderManagementResult<bool>.Ok(true);
        });
    }

    public Task<StoreOrderManagementResult<bool>> BatchUpdateProductStatusAsync(
        BatchUpdateProductStatusInput input
    )
    {
        return transactionExecutor.ExecuteAsync(async () =>
        {
            var productCodes = input.ProductCodes.ToList();
            if (productCodes.Count == 0)
            {
                return StoreOrderManagementResult<bool>.Ok(true);
            }

            var now = DateTime.UtcNow;
            var batchGuid = Guid.NewGuid();
            var beforeSnapshots = await changeHistoryService.CaptureSnapshotsAsync(
                productCodes
            );
            // 与单个开关一致：只改仓库供货状态，不连带商品主档。
            await _db.Updateable<WarehouseProduct>()
                .SetColumns(warehouseProduct => new WarehouseProduct
                {
                    IsActive = input.IsActive,
                    UpdatedAt = now,
                    UpdatedBy = actorContext.ActorName,
                })
                .Where(warehouseProduct =>
                    productCodes.Contains(warehouseProduct.ProductCode)
                    && !warehouseProduct.IsDeleted
                )
                .ExecuteCommandAsync();
            var afterSnapshots = await changeHistoryService.CaptureSnapshotsAsync(
                productCodes
            );
            await changeHistoryService.RecordChangesAsync(
                beforeSnapshots,
                afterSnapshots,
                new WarehouseProductChangeHistoryContextDto
                {
                    Action = "BatchUpdate",
                    Source = "StoreOrderProductStatus",
                    BatchGuid = batchGuid,
                    ActorName = actorContext.ActorName,
                    OccurredAtUtc = now,
                }
            );

            return StoreOrderManagementResult<bool>.Ok(true);
        });
    }
}
