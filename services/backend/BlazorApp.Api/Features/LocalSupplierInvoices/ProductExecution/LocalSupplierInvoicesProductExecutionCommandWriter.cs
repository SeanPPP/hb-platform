using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using SetChildPurchasePriceMutationLock = BlazorApp.Api.Services.ProductCosts.ProductCostMutationLock;
using SetChildPurchasePriceService = BlazorApp.Api.Services.ProductCosts.ProductCostRecalculationService;

namespace BlazorApp.Api.Features.LocalSupplierInvoices
{
    /// <summary>唯一持有批量执行事务、业务锁、锁内复读和写命令的边界。</summary>
    internal sealed class LocalSupplierInvoicesProductExecutionCommandWriter
    {
        private readonly LocalSupplierInvoicesDependencies _dependencies;
        private readonly LocalSupplierInvoicesProductExecutionSource _source;
        private readonly LocalSupplierInvoicesProductExecutionRequestValidator _validator;
        private readonly LocalSupplierInvoicesProductExecutionStore _store;

        public LocalSupplierInvoicesProductExecutionCommandWriter(
            LocalSupplierInvoicesDependencies dependencies,
            LocalSupplierInvoicesProductExecutionSource source,
            LocalSupplierInvoicesProductExecutionRequestValidator validator
        )
        {
            _dependencies = dependencies;
            _source = source;
            _validator = validator;
            _store = new LocalSupplierInvoicesProductExecutionStore(dependencies);
        }

        public async Task<ProductExecutionCommandResult> ExecuteAsync(
            LocalSupplierInvoicesProductExecutionPlan plan
        )
        {
            var db = _dependencies.Context.Db;
            var accumulator = new LocalSupplierInvoicesProductExecutionResultAccumulator();
            await db.Ado.BeginTranAsync();
            try
            {
                // 新建商品必须走总闸；已有商品按稳定排序的父商品编码加锁。
                var lockScope = plan.RequiresAllProductsLock
                    ? await SetChildPurchasePriceMutationLock.AcquireAllAsync(db)
                    : plan.InitialProductCodes.Count > 0
                        ? await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                            db,
                            plan.InitialProductCodes
                        )
                        : null;

                // 锁内重新读取所有执行身份和写入来源，禁止使用等待锁前的快照作决定。
                var lockedData = await _source.ReadLockedAsync(plan.Request);
                if (!plan.TryValidateLockedData(lockedData, out var snapshotError))
                {
                    await db.Ado.RollbackTranAsync();
                    return new ProductExecutionCommandResult(
                        accumulator.Result,
                        snapshotError,
                        "VALIDATION_ERROR"
                    );
                }

                var productCodes = LocalSupplierInvoicesProductExecutionPlan.NormalizeProductCodes(
                    lockedData.Details
                );
                lockScope?.EnsureCovers(db, productCodes);

                var validationErrors = await _validator.ValidateLockedDetailsAsync(
                    lockedData,
                    plan.Request.ProductTypes
                );
                validationErrors.AddRange(plan.Request.ProductTypeSelectionErrors);
                if (validationErrors.Count > 0)
                {
                    accumulator.Result.Failed = validationErrors.Count;
                    accumulator.Result.Errors.AddRange(validationErrors);
                    await db.Ado.RollbackTranAsync();
                    return new ProductExecutionCommandResult(
                        accumulator.Result,
                        "批量执行校验失败",
                        "VALIDATION_ERROR"
                    );
                }

                // 历史审计与主档写入必须处于同一事务，保证任意失败整笔回滚。
                var beforeSnapshots = await _dependencies.ChangeHistoryService.CaptureSnapshotsAsync(productCodes);
                var groups = LocalSupplierInvoicesProductExecutionPlan.GroupBySavedAction(lockedData.Details);
                await ExecuteGroupsAsync(groups, lockedData, plan.Request, accumulator);
                if (accumulator.Result.Failed > 0)
                {
                    await db.Ado.RollbackTranAsync();
                    return new ProductExecutionCommandResult(
                        accumulator.Result,
                        "批量执行失败，已回滚",
                        "BATCH_EXECUTE_ERROR"
                    );
                }

                if (groups.TryGetValue(DetailAction.None, out var none)) accumulator.AddSkipped(none.Count);
                if (groups.TryGetValue(DetailAction.WaitForOperation, out var waiting)) accumulator.AddSkipped(waiting.Count);
                if (accumulator.SuccessfulDetailGuids.Count > 0)
                    await _store.BatchUpdateDetailActivityTypeAsync(
                        accumulator.SuccessfulDetailGuids,
                        plan.Request.UserName
                    );

                var recalculationCodes = SetChildPurchasePriceMutationLock.NormalizeProductCodes(
                    accumulator.ChangedProductCodes
                );
                if (lockScope != null && recalculationCodes.Count > 0)
                {
                    await new SetChildPurchasePriceService(db).RecalculateLockedAsync(
                        lockScope,
                        recalculationCodes,
                        storeCodes: null,
                        plan.Request.UserName
                    );
                }

                if (accumulator.ChangedProductCodes.Count > 0)
                {
                    var afterSnapshots = await _dependencies.ChangeHistoryService.CaptureSnapshotsAsync(
                        accumulator.ChangedProductCodes
                    );
                    await _dependencies.ChangeHistoryService.RecordChangesAsync(
                        beforeSnapshots,
                        afterSnapshots,
                        new WarehouseProductChangeHistoryContextDto
                        {
                            Action = "BatchUpdate",
                            Source = "LocalSupplierInvoice",
                            SourceReference = plan.Request.InvoiceGuid,
                            BatchGuid = Guid.NewGuid(),
                            ActorName = plan.Request.UserName,
                            OccurredAtUtc = DateTime.UtcNow,
                        }
                    );
                }

                await db.Ado.CommitTranAsync();
                return new ProductExecutionCommandResult(accumulator.Result);
            }
            catch
            {
                await db.Ado.RollbackTranAsync();
                throw;
            }
        }

        private async Task ExecuteGroupsAsync(
            IReadOnlyDictionary<DetailAction, List<BlazorApp.Shared.Models.StoreLocalSupplierInvoiceDetails>> groups,
            ProductExecutionSourceData data,
            ProductExecutionRequest request,
            LocalSupplierInvoicesProductExecutionResultAccumulator accumulator
        )
        {
            if (groups.TryGetValue(DetailAction.CreateProduct, out var create))
                accumulator.Apply(
                    DetailAction.CreateProduct,
                    await _store.BatchCreateProductsAsync(
                        create,
                        data.Header!,
                        request.UserName,
                        request.ProductTypes
                    )
                );
            if (groups.TryGetValue(DetailAction.UpdatePurchasePrice, out var prices))
                accumulator.Apply(
                    DetailAction.UpdatePurchasePrice,
                    await _store.BatchUpdatePurchasePriceAsync(prices, request.UserName)
                );
            if (groups.TryGetValue(DetailAction.UpdateItemNumber, out var itemNumbers))
                accumulator.Apply(
                    DetailAction.UpdateItemNumber,
                    await _store.BatchUpdateItemNumberAsync(
                        itemNumbers,
                        data.ProductItemNumbers,
                        request.UserName
                    )
                );
            if (groups.TryGetValue(DetailAction.AddMultiCode, out var multiCodes))
                accumulator.Apply(
                    DetailAction.AddMultiCode,
                    await _store.BatchAddMultiCodesAsync(multiCodes, data.Header!, request.UserName)
                );
        }
    }
}
