using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Domain;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Infrastructure;

internal sealed class ImportPriceVarianceCommandStore(
    SqlSugarContext context,
    IWarehouseProductChangeHistoryService changeHistoryService,
    IStoreOrderProductCostCoordinator productCostCoordinator,
    IStoreOrderActorContext actorContext
)
{
    private readonly ISqlSugarClient _db = context.Db;

    internal async Task<
        ImportPriceVarianceWriteResult<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>
    > UpdateDomesticPriceAsync(ImportPriceVarianceDomesticPriceInput input)
    {
        var currentUser = actorContext.ActorName;
        var batchGuid = Guid.NewGuid();
        var transactionStarted = false;

        try
        {
            await _db.Ado.BeginTranAsync();
            transactionStarted = true;

            var beforeSnapshots = await changeHistoryService.CaptureSnapshotsAsync(
                new[] { input.ProductCode }
            );
            var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                .FirstAsync(wp => wp.ProductCode == input.ProductCode && !wp.IsDeleted);
            if (warehouseProduct == null)
            {
                await _db.Ado.RollbackTranAsync();
                transactionStarted = false;
                return ImportPriceVarianceWriteResult<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>.Fail(
                    "未找到仓库商品，无法更新国内价格"
                );
            }

            // 国内价格命令严格只回写 WarehouseProduct.DomesticPrice。
            warehouseProduct.DomesticPrice = input.DomesticPrice;
            warehouseProduct.UpdatedAt = DateTime.UtcNow;
            warehouseProduct.UpdatedBy = currentUser;
            await _db.Updateable(warehouseProduct)
                .UpdateColumns(wp => new
                {
                    wp.DomesticPrice,
                    wp.UpdatedAt,
                    wp.UpdatedBy,
                })
                .ExecuteCommandAsync();

            var afterSnapshots = await changeHistoryService.CaptureSnapshotsAsync(
                new[] { input.ProductCode }
            );
            await changeHistoryService.RecordChangesAsync(
                beforeSnapshots,
                afterSnapshots,
                CreateHistoryContext(input.ProductCode, batchGuid, currentUser)
            );
            await _db.Ado.CommitTranAsync();
            transactionStarted = false;

            return ImportPriceVarianceWriteResult<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>.Ok(
                new StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto
                {
                    ProductCode = input.ProductCode,
                    DomesticPrice = input.DomesticPrice,
                }
            );
        }
        catch
        {
            if (transactionStarted)
            {
                await _db.Ado.RollbackTranAsync();
            }

            throw;
        }
    }

    internal async Task<
        ImportPriceVarianceWriteResult<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>
    > UpdateWarehouseImportPriceAsync(ImportPriceVarianceWarehouseImportPriceInput input)
    {
        var currentUser = actorContext.ActorName;
        var batchGuid = Guid.NewGuid();
        var transactionStarted = false;

        try
        {
            await _db.Ado.BeginTranAsync();
            transactionStarted = true;

            // 套装成本锁只能在事务内获取；锁规则和统一重算均委托给 Common 协调器。
            var mutationScope = await productCostCoordinator.AcquireProductsAsync(
                new[] { input.ProductCode }
            );
            var beforeSnapshots = await changeHistoryService.CaptureSnapshotsAsync(
                new[] { input.ProductCode }
            );
            // 等待业务锁后复读成本源，避免把锁前旧实体回写到数据库。
            var warehouseProduct = await productCostCoordinator
                .WithUpdateLock(
                    _db.Queryable<WarehouseProduct>()
                        .Where(wp => wp.ProductCode == input.ProductCode && !wp.IsDeleted)
                )
                .FirstAsync();
            if (warehouseProduct == null)
            {
                await _db.Ado.RollbackTranAsync();
                transactionStarted = false;
                return ImportPriceVarianceWriteResult<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>.Fail(
                    "未找到仓库商品，无法更新仓库进货价格"
                );
            }

            var now = DateTime.UtcNow;
            await _db.Updateable<WarehouseProduct>()
                .SetColumns(wp => wp.ImportPrice == input.WarehouseImportPrice)
                .SetColumns(wp => wp.UpdatedAt == now)
                .SetColumns(wp => wp.UpdatedBy == currentUser)
                .Where(wp => wp.ProductCode == input.ProductCode && !wp.IsDeleted)
                .ExecuteCommandAsync();
            await productCostCoordinator.RecalculateAsync(mutationScope, currentUser);

            var afterSnapshots = await changeHistoryService.CaptureSnapshotsAsync(
                new[] { input.ProductCode }
            );
            await changeHistoryService.RecordChangesAsync(
                beforeSnapshots,
                afterSnapshots,
                CreateHistoryContext(input.ProductCode, batchGuid, currentUser)
            );
            await _db.Ado.CommitTranAsync();
            transactionStarted = false;

            return ImportPriceVarianceWriteResult<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>.Ok(
                new StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto
                {
                    ProductCode = input.ProductCode,
                    WarehouseImportPrice = input.WarehouseImportPrice,
                }
            );
        }
        catch
        {
            if (transactionStarted)
            {
                await _db.Ado.RollbackTranAsync();
            }

            throw;
        }
    }

    internal async Task<
        ImportPriceVarianceWriteResult<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>
    > UpdateWarehouseImportPriceBatchAsync(
        ImportPriceVarianceWarehouseImportPriceBatchInput input
    )
    {
        // 保持 SqlSugar 与旧实现相同的 List.Contains 参数化路径及原始选择顺序。
        var productCodes = input.ProductCodes.ToList();
        var batchGuid = Guid.NewGuid();
        var transactionStarted = false;

        try
        {
            await _db.Ado.BeginTranAsync();
            transactionStarted = true;

            var mutationScope = await productCostCoordinator.AcquireProductsAsync(
                productCodes
            );
            // 锁内重读整批仓库成本源，避免批量全实体更新覆盖并发修改。
            var warehouseProducts = await productCostCoordinator
                .WithUpdateLock(
                    _db.Queryable<WarehouseProduct>()
                        .Where(wp =>
                            productCodes.Contains(wp.ProductCode) && !wp.IsDeleted
                        )
                )
                .ToListAsync();
            var existingCodeSet = warehouseProducts
                .Select(wp => wp.ProductCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingCodes = productCodes
                .Where(code => !existingCodeSet.Contains(code))
                .ToList();
            if (missingCodes.Count > 0)
            {
                await _db.Ado.RollbackTranAsync();
                transactionStarted = false;
                return ImportPriceVarianceWriteResult<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>.Fail(
                    $"未找到仓库商品，无法批量更新仓库进货价格：{string.Join(", ", missingCodes)}"
                );
            }

            var currentUser = actorContext.ActorName;
            var beforeSnapshots = await changeHistoryService.CaptureSnapshotsAsync(
                productCodes
            );
            // 批量入口只修改 WarehouseProduct 当前参考进货价，不联动主档、分店价或历史订单。
            var now = DateTime.UtcNow;
            await _db.Updateable<WarehouseProduct>()
                .SetColumns(wp => wp.ImportPrice == input.WarehouseImportPrice)
                .SetColumns(wp => wp.UpdatedAt == now)
                .SetColumns(wp => wp.UpdatedBy == currentUser)
                .Where(wp => productCodes.Contains(wp.ProductCode) && !wp.IsDeleted)
                .ExecuteCommandAsync();
            await productCostCoordinator.RecalculateAsync(mutationScope, currentUser);

            var afterSnapshots = await changeHistoryService.CaptureSnapshotsAsync(
                productCodes
            );
            await changeHistoryService.RecordChangesAsync(
                beforeSnapshots,
                afterSnapshots,
                CreateHistoryContext(
                    string.Join(",", productCodes),
                    batchGuid,
                    currentUser
                )
            );
            await _db.Ado.CommitTranAsync();
            transactionStarted = false;

            return ImportPriceVarianceWriteResult<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>.Ok(
                new StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto
                {
                    UpdatedCount = warehouseProducts.Count,
                    WarehouseImportPrice = input.WarehouseImportPrice,
                    ProductCodes = productCodes,
                }
            );
        }
        catch
        {
            if (transactionStarted)
            {
                await _db.Ado.RollbackTranAsync();
            }

            throw;
        }
    }

    private static WarehouseProductChangeHistoryContextDto CreateHistoryContext(
        string sourceReferenceSuffix,
        Guid batchGuid,
        string currentUser
    )
    {
        return new WarehouseProductChangeHistoryContextDto
        {
            Action = "Update",
            Source = "StoreOrderImportPriceVariance",
            SourceReference = $"StoreOrderImportPriceVariance:{sourceReferenceSuffix}",
            BatchGuid = batchGuid,
            ActorName = currentUser,
        };
    }
}
