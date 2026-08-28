using System.Collections.Concurrent;
using System.Data;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.DataSync.Common;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using SqlSugar;

namespace BlazorApp.Api.Features.DataSync.Full.Stores;

/// <summary>
/// DataSyncStoreProductsStore 保留旧同步步骤的顺序，仅负责本切片的持久化与读取。
/// </summary>
internal sealed class DataSyncStoreProductsStore : DataSyncSliceBase
{
    public DataSyncStoreProductsStore(DataSyncSliceContext context)
        : base(context)
    {
    }

        public async Task<SyncResult> SyncStoreClearancePricesFromHqAsync(
            List<string>? selectedStoreCodes = null
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };

            try
            {
                Logger.LogInformation(
                    $"🔄 开始从HQ数据库同步分店清货价数据{(selectedStoreCodes?.Any() == true ? $"，指定分店: {string.Join(", ", selectedStoreCodes)}" : "，全部分店")}"
                );

                // 🚀 使用JOIN查询获取有效的HQ数据（商品信息存在且分店代码匹配）
                var query = HqContext
                    .Db.Queryable<DIC_商品清货价表, DIC_商品信息字典表>(
                        (clearance, product) =>
                            new JoinQueryInfos(
                                JoinType.Inner,
                                clearance.商品编码 == product.H商品编码
                            )
                    )
                    .Where(
                        (clearance, product) =>
                            !string.IsNullOrEmpty(clearance.商品编码)
                            && !string.IsNullOrEmpty(clearance.分店代码)
                            && product.H使用状态 == true
                    );

                // 如果指定了分店代码，添加分店代码过滤条件
                if (selectedStoreCodes?.Any() == true)
                {
                    query = query.Where(
                        (clearance, product) => selectedStoreCodes.Contains(clearance.分店代码)
                    );
                }

                var hqClearancePrices = await query
                    .Select((clearance, product) => clearance)
                    .ToListAsync();

                Logger.LogInformation(
                    $"📊 从HQ获取到 {hqClearancePrices.Count:N0} 条有效的分店清货价记录（已过滤无效商品和分店）"
                );

                if (!hqClearancePrices.Any())
                {
                    result.Message = "✅ HQ数据库中没有分店清货价数据，同步完成";
                    result.IsSuccess = true;
                    return result;
                }

                // 开始数据库事务
                var db = LocalContext.Db;
                await db.Ado.BeginTranAsync();

                try
                {
                    // 根据是否指定分店来决定删除策略
                    if (selectedStoreCodes?.Any() == true)
                    {
                        Logger.LogInformation(
                            $"🗑️ 正在清空指定分店的清货价数据: {string.Join(", ", selectedStoreCodes)}"
                        );
                        await db.Deleteable<StoreClearancePrice>()
                            .Where(x =>
                                x.StoreCode != null && selectedStoreCodes.Contains(x.StoreCode)
                            )
                            .ExecuteCommandAsync();
                        Logger.LogInformation("✅ 指定分店的清货价数据已清空");
                    }
                    else
                    {
                        Logger.LogInformation("🗑️ 正在清空本地分店清货价表...");
                        await db.Deleteable<StoreClearancePrice>().ExecuteCommandAsync();
                        Logger.LogInformation("✅ 本地分店清货价表已清空");
                    }

                    int totalProcessed = 0;
                    int totalAdded = 0;
                    const int batchSize = 10000;

                    // 转换数据 - 使用AutoMapper
                    Logger.LogInformation("🔄 开始转换数据格式 (使用AutoMapper)...");
                    var localClearancePrices = Mapper.Map<List<StoreClearancePrice>>(
                        hqClearancePrices
                    );
                    Logger.LogInformation(
                        $"✅ 数据转换完成，共 {localClearancePrices.Count:N0} 条记录"
                    );

                    // 使用PageSize优化的批量插入
                    Logger.LogInformation(
                        $"📦 开始批量插入 {localClearancePrices.Count:N0} 条记录..."
                    );
                    // BulkCopy 异常必须交给外层唯一事务边界回滚，不能吞掉后再提交删除结果。
                    await db.Fastest<StoreClearancePrice>()
                        .PageSize(batchSize) // 让SqlSugar内部处理分批
                        .BulkCopyAsync(localClearancePrices);

                    totalAdded = localClearancePrices.Count;
                    totalProcessed = localClearancePrices.Count;

                    Logger.LogInformation($"✅ 批量插入完成，成功插入 {totalAdded:N0} 条记录");

                    // 提交事务
                    await db.Ado.CommitTranAsync();
                    Logger.LogInformation("✅ 事务提交成功");

                    result.AddedCount = totalAdded;
                    result.ErrorCount = 0;
                    result.IsSuccess = true;
                    result.Message = $"🎉 分店清货价数据同步成功！共处理 {totalProcessed:N0} 条记录，全部成功插入";

                    Logger.LogInformation(result.Message);
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    Logger.LogError(ex, "❌ 事务回滚，同步失败");
                    throw;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "❌ 同步分店清货价数据时发生错误");
                result.AddedCount = 0;
                result.ErrorCount = 1;
                result.Message = $"❌ 同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
                Logger.LogInformation($"⏱️ 同步耗时: {result.Duration.TotalSeconds:F1} 秒");
            }

            return result;
        }

        public async Task<SyncResult> SyncStoreMultiCodeProductsFromHqAsync(
            List<string>? selectedStoreCodes = null
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };

            try
            {
                Logger.LogInformation(
                    $"🔄 开始从HQ数据库同步分店一品多码数据{(selectedStoreCodes?.Any() == true ? $"，指定分店: {string.Join(", ", selectedStoreCodes)}" : "，全部分店")}"
                );

                // 🚀 使用JOIN查询获取有效的HQ数据（商品信息存在且分店代码匹配）
                var query = HqContext
                    .Db.Queryable<DIC_分店一品多码表, DIC_商品信息字典表>(
                        (multiCode, product) =>
                            new JoinQueryInfos(
                                JoinType.Inner,
                                multiCode.H商品编码 == product.H商品编码
                            )
                    )
                    .Where(
                        (multiCode, product) =>
                            !string.IsNullOrEmpty(multiCode.H商品编码)
                            && !string.IsNullOrEmpty(multiCode.H分店代码)
                            && multiCode.H使用状态 == true
                            && product.H使用状态 == true
                    );

                // 如果指定了分店代码，添加分店代码过滤条件
                if (selectedStoreCodes?.Any() == true)
                {
                    query = query.Where(
                        (multiCode, product) =>
                            !string.IsNullOrEmpty(multiCode.H分店代码)
                            && selectedStoreCodes.Contains(multiCode.H分店代码)
                    );
                }

                var hqMultiCodeProducts = await query
                    .Select((multiCode, product) => multiCode)
                    .ToListAsync();

                Logger.LogInformation(
                    $"📊 从HQ获取到 {hqMultiCodeProducts.Count:N0} 条有效的分店一品多码记录（已过滤无效商品和分店）"
                );

                if (!hqMultiCodeProducts.Any())
                {
                    result.Message = "✅ HQ数据库中没有分店一品多码数据，同步完成";
                    result.IsSuccess = true;
                    return result;
                }

                // 开始数据库事务
                var db = LocalContext.Db;
                await db.Ado.BeginTranAsync();

                try
                {
                    var childCostLockScope = await SetChildPurchasePriceMutationLock.AcquireAllAsync(
                        db
                    );
                    // 所有状态的本地 Type1 关系都保护对应门店投影，不能被 HQ 普通多码覆盖。
                    var protectedStoreMultiCodeKeys = await DataSyncProductProtectionRules.GetProtectedStoreMultiCodeKeysAsync(db);
                    var normalizedSelectedStoreCodes = selectedStoreCodes?
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Select(code => code.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList() ?? [];
                    var existingScopeQuery = db.Queryable<StoreMultiCodeProduct>();
                    if (normalizedSelectedStoreCodes.Count > 0)
                    {
                        existingScopeQuery = existingScopeQuery.Where(item =>
                            normalizedSelectedStoreCodes.Contains(item.StoreCode!)
                        );
                    }
                    var existingScopeRows = await existingScopeQuery.ToListAsync();

                    Logger.LogInformation("🗑️ 正在清空本地分店一品多码表（保留本地组合套装子项）...");
                    var deleteable = db
                        .Deleteable<StoreMultiCodeProduct>()
                        .Where(x =>
                            !SqlFunc
                                .Subqueryable<ProductSetCode>()
                                .Where(setCode =>
                                    setCode.SetType == 1
                                    && setCode.ProductCode != null
                                    && setCode.SetProductCode != null
                                    && x.ProductCode != null
                                    && x.MultiCodeProductCode != null
                                    && SqlFunc.ToUpper(setCode.ProductCode.Trim())
                                        == SqlFunc.ToUpper(x.ProductCode.Trim())
                                    && SqlFunc.ToUpper(setCode.SetProductCode.Trim())
                                        == SqlFunc.ToUpper(x.MultiCodeProductCode.Trim())
                                )
                                .Any()
                        );
                    if (normalizedSelectedStoreCodes.Count > 0)
                    {
                        deleteable = deleteable.Where(x =>
                            normalizedSelectedStoreCodes.Contains(x.StoreCode!)
                        );
                    }
                    await deleteable.ExecuteCommandAsync();
                    Logger.LogInformation("✅ 本地分店一品多码表已清理（组合套装子项已保留）");

                    int totalProcessed = 0;
                    int totalAdded = 0;
                    int totalErrors = 0;
                    const int batchSize = 20000;

                    // 转换数据 - 使用AutoMapper
                    Logger.LogInformation("🔄 开始转换数据格式 (使用AutoMapper)...");
                    var localMultiCodeProducts = Mapper.Map<List<StoreMultiCodeProduct>>(
                        hqMultiCodeProducts
                    )
                        // 受保护行不接受 HQ PurchasePrice 或其他字段写入，避免被同步结果覆盖。
                        .Where(item => !protectedStoreMultiCodeKeys.Contains(DataSyncProductProtectionRules.GetStoreMultiCodeBusinessKey(item)))
                        .ToList();
                    foreach (var item in localMultiCodeProducts)
                    {
                        item.PurchasePrice = null;
                    }
                    var affectedStoreProductGroups = existingScopeRows
                        .Where(item =>
                            !protectedStoreMultiCodeKeys.Contains(
                                DataSyncProductProtectionRules.GetStoreMultiCodeBusinessKey(item)
                            )
                        )
                        .Concat(localMultiCodeProducts)
                        .Where(item =>
                            !string.IsNullOrWhiteSpace(item.StoreCode)
                            && !string.IsNullOrWhiteSpace(item.ProductCode)
                        )
                        .Select(item => (item.StoreCode, item.ProductCode))
                        .Distinct()
                        .ToList();
                    Logger.LogInformation(
                        $"✅ 数据转换完成，共 {localMultiCodeProducts.Count:N0} 条记录"
                    );

                    // 使用PageSize优化的批量插入
                    Logger.LogInformation(
                        $"📦 开始批量插入 {localMultiCodeProducts.Count:N0} 条记录..."
                    );
                    try
                    {
                        await db.Fastest<StoreMultiCodeProduct>()
                            .PageSize(batchSize) // 让SqlSugar内部处理分批
                            .BulkCopyAsync(localMultiCodeProducts);

                        totalAdded = localMultiCodeProducts.Count;
                        totalProcessed = localMultiCodeProducts.Count;

                        Logger.LogInformation($"✅ 批量插入完成，成功插入 {totalAdded:N0} 条记录");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"❌ 批量插入失败，错误: {ex.Message}");
                        totalErrors = localMultiCodeProducts.Count;
                        throw;
                    }

                    if (affectedStoreProductGroups.Count > 0)
                    {
                        var recalculation = await new SetChildPurchasePriceService(db)
                            .RecalculateStoreGroupsLockedAsync(
                                childCostLockScope,
                                affectedStoreProductGroups,
                                HistoryContextFactory.ResolveSetChildPurchasePriceActor()
                            );
                        DataSyncProductProtectionRules.EnsureSetChildPurchasePriceRecalculated(
                            recalculation,
                            affectedStoreProductGroups.Select(group => group.ProductCode!)
                        );
                    }

                    // 提交事务
                    await db.Ado.CommitTranAsync();
                    Logger.LogInformation("✅ 事务提交成功");

                    result.AddedCount = totalAdded;
                    result.ErrorCount = totalErrors;
                    result.IsSuccess = totalErrors == 0;
                    result.Message =
                        totalErrors == 0
                            ? $"🎉 分店一品多码数据同步成功！共处理 {totalProcessed:N0} 条记录，全部成功插入"
                            : $"⚠️ 分店一品多码数据同步部分成功！成功: {totalAdded:N0}, 失败: {totalErrors:N0}";

                    Logger.LogInformation(result.Message);
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    Logger.LogError(ex, "❌ 事务回滚，同步失败");
                    throw;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "❌ 同步分店一品多码数据时发生错误");
                result.IsSuccess = false;
                if (SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out var conflict))
                {
                    result.ErrorCode = SetChildPurchasePriceMutationLock.BusyErrorCode;
                    result.Message = conflict!.Message;
                }
                else
                {
                    result.Message = $"❌ 同步失败: {ex.Message}";
                }
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
                Logger.LogInformation($"⏱️ 同步耗时: {result.Duration.TotalSeconds:F1} 秒");
            }

            return result;
        }
}
