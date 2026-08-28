using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.Orders.Domain;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.Orders.Infrastructure;

internal sealed class StoreOrderCommandStore(
    SqlSugarContext context,
    IStoreOrderActorContext actorContext,
    StoreOrderStoreIdentityReader storeIdentityReader
)
{
    private readonly ISqlSugarClient _db = context.Db;

    internal Task<StoreOrderOrdersWriteResult<StoreOrderStoreContactDto>> UpdateStoreContactAsync(
        UpdateStoreContactInput input
    )
    {
        return ExecuteInSingleTransactionAsync(async () =>
        {
            var order = await _db.Queryable<WareHouseOrder>()
                .Where(candidate =>
                    candidate.OrderGUID == input.OrderGuid && !candidate.IsDeleted
                )
                .FirstAsync();
            if (order == null)
            {
                return StoreOrderOrdersWriteResult<StoreOrderStoreContactDto>.Fail(
                    "订单不存在",
                    "STORE_ORDER_NOT_FOUND"
                );
            }

            var store = await GetStoreByCodeOrGuidAsync(input.StoreCode);
            if (store == null)
            {
                return StoreOrderOrdersWriteResult<StoreOrderStoreContactDto>.Fail(
                    "分店不存在",
                    "STORE_NOT_FOUND"
                );
            }
            if (
                !StoreOrderOrdersRules.SameText(order.StoreCode, store.StoreCode)
                && !StoreOrderOrdersRules.SameText(order.StoreCode, store.StoreGUID)
            )
            {
                return StoreOrderOrdersWriteResult<StoreOrderStoreContactDto>.Fail(
                    "订单与分店不匹配",
                    "STORE_ORDER_STORE_MISMATCH"
                );
            }

            // null 表示保持旧值；空字符串继续表示显式清空。
            store.Address = input.Address == null
                ? store.Address
                : StoreOrderOrdersRules.TrimLen(input.Address, 500);
            store.ContactEmail = input.ContactEmail == null
                ? store.ContactEmail
                : StoreOrderOrdersRules.NormalizeOptionalEmail(input.ContactEmail);
            store.UpdatedAt = DateTime.UtcNow;
            store.UpdatedBy = actorContext.ActorName;
            await _db.Updateable(store)
                .UpdateColumns(candidate => new
                {
                    candidate.Address,
                    candidate.ContactEmail,
                    candidate.UpdatedAt,
                    candidate.UpdatedBy,
                })
                .ExecuteCommandAsync();

            return StoreOrderOrdersWriteResult<StoreOrderStoreContactDto>.Ok(
                new StoreOrderStoreContactDto
                {
                    OrderGUID = input.OrderGuid,
                    StoreCode = store.StoreCode,
                    Address = store.Address,
                    ContactEmail = store.ContactEmail,
                }
            );
        });
    }

    internal Task<
        StoreOrderOrdersWriteResult<BatchMapStoreOrderStoreCodeResultDto>
    > BatchMapStoreOrderStoreCodeAsync(BatchMapStoreOrderStoreCodeInput input)
    {
        return ExecuteInSingleTransactionAsync(async () =>
        {
            var targetCodes = input.Mappings
                .Select(mapping => mapping.TargetStoreCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            // 历史修复允许映射到停用但未删除的分店，最终只写本地 StoreCode。
            var targetStores = await _db.Queryable<Store>()
                .Where(store => targetCodes.Contains(store.StoreCode) && !store.IsDeleted)
                .Select(store => store.StoreCode)
                .ToListAsync();
            var targetStoreSet = targetStores.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingTargets = targetCodes
                .Where(code => !targetStoreSet.Contains(code))
                .ToList();
            if (missingTargets.Count > 0)
            {
                return StoreOrderOrdersWriteResult<BatchMapStoreOrderStoreCodeResultDto>.Fail(
                    $"目标分店不存在：{string.Join(", ", missingTargets)}"
                );
            }

            var unmatchedSourceSet =
                await storeIdentityReader.GetUnmatchedOrderStoreCodesAsync();
            var currentUser = actorContext.ActorName;
            var now = DateTime.Now;
            var result = new BatchMapStoreOrderStoreCodeResultDto();
            foreach (var mapping in input.Mappings)
            {
                var itemResult = new StoreOrderStoreCodeMappingResultItemDto
                {
                    SourceStoreCode = mapping.SourceStoreCode,
                    TargetStoreCode = mapping.TargetStoreCode,
                };
                var sourceCount = await _db.Queryable<WareHouseOrder>()
                    .Where(order =>
                        !order.IsDeleted
                        && order.StoreCode == mapping.SourceStoreCode
                    )
                    .CountAsync();
                if (!unmatchedSourceSet.Contains(mapping.SourceStoreCode))
                {
                    itemResult.SkippedCount = sourceCount;
                    result.SkippedCount += itemResult.SkippedCount;
                    result.Items.Add(itemResult);
                    continue;
                }

                itemResult.UpdatedCount = await _db.Updateable<WareHouseOrder>()
                    .SetColumns(order => new WareHouseOrder
                    {
                        StoreCode = mapping.TargetStoreCode,
                        UpdatedBy = currentUser,
                        UpdatedAt = now,
                    })
                    .Where(order =>
                        !order.IsDeleted
                        && order.StoreCode == mapping.SourceStoreCode
                    )
                    .ExecuteCommandAsync();
                itemResult.SkippedCount = Math.Max(
                    0,
                    sourceCount - itemResult.UpdatedCount
                );
                result.UpdatedCount += itemResult.UpdatedCount;
                result.SkippedCount += itemResult.SkippedCount;
                result.Items.Add(itemResult);
            }

            return StoreOrderOrdersWriteResult<BatchMapStoreOrderStoreCodeResultDto>.Ok(
                result
            );
        });
    }

    private async Task<Store?> GetStoreByCodeOrGuidAsync(string storeCode)
    {
        if (string.IsNullOrWhiteSpace(storeCode))
        {
            return null;
        }

        var normalizedStoreCode = storeCode.Trim();
        return await _db.Queryable<Store>()
            .Where(store =>
                !store.IsDeleted
                && (
                    store.StoreCode == normalizedStoreCode
                    || store.StoreGUID == normalizedStoreCode
                )
            )
            .FirstAsync();
    }

    private async Task<StoreOrderOrdersWriteResult<T>> ExecuteInSingleTransactionAsync<T>(
        Func<Task<StoreOrderOrdersWriteResult<T>>> action
    )
        where T : class
    {
        var transactionStarted = false;
        try
        {
            await _db.Ado.BeginTranAsync();
            transactionStarted = true;
            var result = await action();
            if (result.Success)
            {
                await _db.Ado.CommitTranAsync();
            }
            else
            {
                await _db.Ado.RollbackTranAsync();
            }
            transactionStarted = false;
            return result;
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
}
