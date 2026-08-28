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
            var affectedRows = await _db.Updateable<Product>()
                .SetColumns(product => new Product
                {
                    IsActive = input.IsActive,
                    UpdatedAt = now,
                    UpdatedBy = actorContext.ActorName,
                })
                .Where(product => product.ProductCode == input.ProductCode)
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
            await _db.Updateable<Product>()
                .SetColumns(product => new Product
                {
                    IsActive = input.IsActive,
                    UpdatedAt = now,
                    UpdatedBy = actorContext.ActorName,
                })
                .Where(product =>
                    product.ProductCode != null
                    && productCodes.Contains(product.ProductCode)
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
