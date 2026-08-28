using System.Collections.Concurrent;
using System.Data;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.DataSync.Common;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using SqlSugar;

namespace BlazorApp.Api.Features.DataSync.Full.Products;

/// <summary>
/// DataSyncProductsStore 保留旧同步步骤的顺序，仅负责本切片的持久化与读取。
/// </summary>
internal sealed class DataSyncProductsStore : DataSyncSliceBase
{
    public DataSyncProductsStore(DataSyncSliceContext context)
        : base(context)
    {
    }

        public async Task<SyncResult> SyncProductsFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
                AddedCount = 0,
                UpdatedCount = 0,
                ErrorCount = 0,
            };
            var batchGuid = Guid.NewGuid();

            try
            {
                Logger.LogInformation(
                    "🚀 开始从HQ数据库同步商品信息数据（包括商品字典和一品多码表到ProductSetCode）..."
                );

                // 1. 检查HQ数据库连接
                HqContext.CheckConnection();

                // 🔄 开启事务，确保数据一致性
                await LocalContext.Db.Ado.BeginTranAsync();

                try
                {
                    var childCostLockScope = await SetChildPurchasePriceMutationLock.AcquireAllAsync(
                        LocalContext.Db
                    );
                    // 事务内先批量预取旧快照，确保清空重建与历史写入使用同一事务。
                    var existingProductCodes = await LocalContext
                        .Db.Queryable<Product>()
                        .Where(item => item.ProductCode != null)
                        .Select(item => item.ProductCode)
                        .ToListAsync();
                    var auditProductCodes = new HashSet<string>(
                        existingProductCodes
                            .Where(code => !string.IsNullOrWhiteSpace(code))
                            .Select(code => code!.Trim()),
                        StringComparer.OrdinalIgnoreCase
                    );
                    var beforeSnapshots = await ChangeHistoryService.CaptureSnapshotsAsync(
                        auditProductCodes
                    );

                    // 2. 先清空相关表数据。任何状态的本地 Type1 都不能被 HQ 普通多码复用或降级。
                    var protectedType1 = await DataSyncProductProtectionRules.GetAllType1ProtectionAsync(LocalContext.Db);
                    var protectedSetParentCodes = protectedType1.ProductCodes.ToList();
                    Logger.LogInformation("清理本地ProductSetCode表（保留组合套装子项）和Product表数据...");
                    await LocalContext
                        .Db.Deleteable<ProductSetCode>()
                        .Where(x => x.SetType != 1)
                        .ExecuteCommandAsync();
                    await LocalContext
                        .Db.Deleteable<Product>()
                        .AS("Product")
                        .ExecuteCommandAsync();
                    Logger.LogInformation("已清理本地ProductSetCode表和Product表数据");

                    // 3. 从HQ数据库获取商品信息数据 (使用批量操作)
                    const int batchSize = 50000; // 每批处理50000条记录，避免超时和内存问题
                    var totalProcessed = 0;
                    var totalProductAdded = 0;
                    var totalErrors = 0;
                    var pageNumber = 1;

                    Logger.LogInformation("开始同步商品字典表数据...");

                    while (true)
                    {
                        var hqProductsBatch = await HqContext
                            .DIC_商品信息字典表Db.AsQueryable()
                            .Skip((pageNumber - 1) * batchSize)
                            .Take(batchSize)
                            .ToListAsync();

                        if (!hqProductsBatch.Any())
                            break; // 没有更多数据

                        Logger.LogInformation(
                            $"从HQ数据库获取到第 {pageNumber} 批商品信息，共 {hqProductsBatch.Count} 条"
                        );

                        try
                        {
                            // 🚀 处理Product数据
                            // 1. 转换为Product实体
                            var localProducts = hqProductsBatch
                                .Select(hqProduct => Mapper.Map<Product>(hqProduct))
                                .ToList();
                            foreach (var product in localProducts)
                            {
                                if (!string.IsNullOrWhiteSpace(product.ProductCode))
                                {
                                    auditProductCodes.Add(product.ProductCode.Trim());
                                }
                            }

                            // 2. 批量插入Product数据
                            await LocalContext
                                .Db.Fastest<Product>()
                                .AS("Product")
                                .PageSize(10000) // 减小页面大小，避免超时
                                .BulkCopyAsync(localProducts);

                            totalProductAdded += localProducts.Count;
                            totalProcessed += hqProductsBatch.Count;

                            Logger.LogInformation(
                                $"第 {pageNumber} 批处理完成 - Product: {localProducts.Count} 条"
                            );

                            // 每处理一批后稍微延迟，避免数据库压力过大
                            if (pageNumber % 5 == 0)
                            {
                                await Task.Delay(1000); // 每5批延迟1秒
                                Logger.LogInformation(
                                    $"已处理 {pageNumber} 批数据，总计 {totalProductAdded} 条Product记录"
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, $"第 {pageNumber} 批商品数据处理失败");
                            totalErrors += hqProductsBatch.Count;
                            // 任意一页失败都必须让本次全量同步整体回滚。
                            throw;
                        }

                        pageNumber++;
                    }

                    Logger.LogInformation($"商品字典表同步完成 - Product: {totalProductAdded} 条");

                    // 🚀 4. 使用JOIN连接查询同步一品多码表数据到ProductSetCode（只同步与已同步商品相关的记录）
                    Logger.LogInformation(
                        "开始使用JOIN连接查询同步一品多码表数据到ProductSetCode..."
                    );

                    var totalMultiCodeAdded = 0;
                    var affectedSetParentCodes = new HashSet<string>(
                        protectedSetParentCodes,
                        StringComparer.OrdinalIgnoreCase
                    );
                    pageNumber = 1;

                    while (true)
                    {
                        // 🔧 优化策略：使用JOIN连接查询，直接关联商品信息表和一品多码表
                        // 这样可以避免生成超长的IN语句，提高查询性能
                        var hqMultiCodesBatch = await HqContext
                            .Db.Queryable<
                                BlazorApp.Shared.Models.HqEntities.DIC_一品多码表,
                                BlazorApp.Shared.Models.HqEntities.DIC_商品信息字典表
                            >(
                                (multiCode, product) =>
                                    new JoinQueryInfos(
                                        JoinType.Inner,
                                        multiCode.H商品编码 == product.H商品编码
                                    )
                            )
                            .Where(
                                (multiCode, product) =>
                                    !string.IsNullOrEmpty(multiCode.H多码商品编号)
                                    && !string.IsNullOrEmpty(multiCode.H商品编码)
                            )
                            .Select((multiCode, product) => multiCode)
                            .Skip((pageNumber - 1) * batchSize)
                            .Take(batchSize)
                            .ToListAsync();

                        if (!hqMultiCodesBatch.Any())
                            break; // 没有更多数据

                        Logger.LogInformation(
                            $"从HQ数据库获取到第 {pageNumber} 批一品多码数据，共 {hqMultiCodesBatch.Count} 条"
                        );

                        try
                        {
                            // HQ 成本不是最终值；同时按 GUID 与规范化父子键保护所有状态的本地 Type1。
                            var multiCodeSetCodes = new List<ProductSetCode>();
                            foreach (var hqMultiCode in hqMultiCodesBatch)
                            {
                                if (
                                    DataSyncProductProtectionRules.TryGetProtectedType1Conflict(
                                        hqMultiCode,
                                        protectedType1,
                                        out var conflictReason
                                    )
                                )
                                {
                                    Logger.LogWarning(
                                        "跳过与本地 Type1 冲突的 HQ 多码。ProductCode={ProductCode}, ChildCode={ChildCode}, HGUID={Hguid}, Reason={Reason}",
                                        hqMultiCode.H商品编码,
                                        hqMultiCode.H多码商品编号,
                                        hqMultiCode.HGUID,
                                        conflictReason
                                    );
                                    continue;
                                }

                                var mapped = Mapper.Map<ProductSetCode>(hqMultiCode);
                                mapped.SetPurchasePrice = null;
                                multiCodeSetCodes.Add(mapped);
                                DataSyncProductProtectionRules.AddNormalizedCode(affectedSetParentCodes, mapped.ProductCode);
                            }

                            // 批量插入ProductSetCode数据（多码信息）
                            await LocalContext
                                .Db.Insertable(multiCodeSetCodes)
                                .PageSize(2000) // 进一步减小页面大小，避免超时
                                .ExecuteCommandAsync();

                            totalMultiCodeAdded += multiCodeSetCodes.Count;
                            Logger.LogInformation(
                                $"第 {pageNumber} 批一品多码数据处理完成 - ProductSetCode: {multiCodeSetCodes.Count} 条"
                            );

                            // 每处理一批后稍微延迟，避免数据库压力过大
                            if (pageNumber % 3 == 0)
                            {
                                await Task.Delay(1500); // 每3批延迟1.5秒
                                Logger.LogInformation(
                                    $"已处理 {pageNumber} 批一品多码数据，总计 {totalMultiCodeAdded} 条ProductSetCode记录"
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, $"第 {pageNumber} 批一品多码数据处理失败");
                            totalErrors += hqMultiCodesBatch.Count;
                            // 任意一页失败都必须让本次全量同步整体回滚。
                            throw;
                        }

                        pageNumber++;
                    }

                    Logger.LogInformation(
                        $"一品多码表同步完成 - ProductSetCode: {totalMultiCodeAdded} 条"
                    );

                    if (affectedSetParentCodes.Count > 0)
                    {
                        var recalculation = await new SetChildPurchasePriceService(LocalContext.Db)
                            .RecalculateLockedAsync(
                                childCostLockScope,
                                affectedSetParentCodes,
                                null,
                                HistoryContextFactory.ResolveSetChildPurchasePriceActor()
                            );
                        DataSyncProductProtectionRules.EnsureSetChildPurchasePriceRecalculated(
                            recalculation,
                            affectedSetParentCodes
                        );
                    }

                    var afterSnapshots = await ChangeHistoryService.CaptureSnapshotsAsync(
                        auditProductCodes
                    );
                    await ChangeHistoryService.RecordChangesAsync(
                        beforeSnapshots,
                        afterSnapshots,
                        HistoryContextFactory.Create(
                            "DataSyncLegacyFull",
                            batchGuid,
                            DateTime.UtcNow
                        )
                    );

                    // 🎉 提交事务
                    await LocalContext.Db.Ado.CommitTranAsync();

                    result.AddedCount = totalProductAdded + totalMultiCodeAdded;
                    result.UpdatedCount = 0; // 由于是先删除再插入，所以没有更新操作
                    result.ErrorCount = totalErrors;
                    // 有任一批次错误时不得把部分同步伪装成成功，供调用方统一返回失败包络。
                    result.IsSuccess = totalErrors == 0;
                    result.Message =
                        $"🎉 商品信息同步完成！Product表: {totalProductAdded} 条，ProductSetCode表: {totalMultiCodeAdded} 条（多码），错误: {totalErrors} 条";
                    Logger.LogInformation(result.Message);
                }
                catch (Exception)
                {
                    // 🔙 回滚事务
                    await LocalContext.Db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步商品信息数据时发生错误");
                result.IsSuccess = false;
                // 锁竞争前已回滚整个事务；显式标记 busy，避免调用方把它当成数据质量跳过。
                if (SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out var conflict))
                {
                    result.ErrorCode = SetChildPurchasePriceMutationLock.BusyErrorCode;
                    result.Message = conflict!.Message;
                }
                else
                {
                    result.Message = $"同步失败: {ex.Message}";
                }
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        private async Task<DataSyncProductBatchResult> ProcessProductBatchAsync(
            List<Product> localProducts,
            int pageNumber,
            SemaphoreSlim? semaphore
        )
        {
            // 如果使用了并发控制，则等待信号量
            if (semaphore != null)
            {
                await semaphore.WaitAsync();
            }

            try
            {
                // 执行BulkCopy操作，添加重试机制
                const int maxRetries = 3;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        // 检查并确保数据库连接是打开的
                        if (LocalContext.Db.Ado.Transaction == null)
                        {
                            // 如果没有事务，确保连接是可用的
                            if (
                                LocalContext.Db.Ado.Connection.State
                                != System.Data.ConnectionState.Open
                            )
                            {
                                var dbConnection =
                                    LocalContext.Db.Ado.Connection
                                    as System.Data.Common.DbConnection;
                                if (dbConnection != null)
                                {
                                    await dbConnection.OpenAsync();
                                }
                                else
                                {
                                    LocalContext.Db.Ado.Connection.Open();
                                }
                            }
                        }
                        else
                        {
                            // 即使在事务中，也要确保连接是打开的
                            if (
                                LocalContext.Db.Ado.Connection.State
                                != System.Data.ConnectionState.Open
                            )
                            {
                                var dbConnection =
                                    LocalContext.Db.Ado.Connection
                                    as System.Data.Common.DbConnection;
                                if (dbConnection != null)
                                {
                                    await dbConnection.OpenAsync();
                                }
                                else
                                {
                                    LocalContext.Db.Ado.Connection.Open();
                                }
                            }
                        }

                        // 再次检查连接状态
                        if (
                            LocalContext.Db.Ado.Connection.State
                            != System.Data.ConnectionState.Open
                        )
                        {
                            throw new InvalidOperationException("无法打开数据库连接");
                        }

                        // 使用BulkCopy插入数据
                        await LocalContext
                            .Db.Fastest<Product>()
                            .AS("Product")
                            .PageSize(50000)
                            .BulkCopyAsync(localProducts);

                        Logger.LogInformation(
                            $"第 {pageNumber} 批商品信息BulkCopy操作完成，插入 {localProducts.Count} 条记录"
                        );

                        return new DataSyncProductBatchResult
                        {
                            IsSuccess = true,
                            ProcessedCount = localProducts.Count,
                            PageNumber = pageNumber,
                        };
                    }
                    catch (Exception ex)
                        when (attempt < maxRetries
                            && (
                                ex.Message.Contains("连接被关闭")
                                || ex.Message.Contains("connection is closed")
                                || ex.Message.Contains("Invalid operation")
                                || ex is System.InvalidOperationException
                                || ex.Message.Contains("Connection closed")
                                || ex.Message.Contains("closed connection")
                            )
                        )
                    {
                        Logger.LogWarning(
                            $"第 {pageNumber} 批商品信息BulkCopy操作失败 (尝试 {attempt}/{maxRetries}): {ex.Message}"
                        );
                        // 等待一段时间后重试
                        await Task.Delay(1000 * attempt);
                    }
                }

                // 如果所有重试都失败了
                Logger.LogError($"第 {pageNumber} 批商品信息BulkCopy操作最终失败");
                return new DataSyncProductBatchResult
                {
                    IsSuccess = false,
                    ProcessedCount = localProducts.Count,
                    PageNumber = pageNumber,
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"第 {pageNumber} 批商品信息BulkCopy操作失败");
                return new DataSyncProductBatchResult
                {
                    IsSuccess = false,
                    ProcessedCount = localProducts.Count,
                    PageNumber = pageNumber,
                };
            }
            finally
            {
                // 如果使用了并发控制，则释放信号量
                if (semaphore != null)
                {
                    semaphore.Release();
                }
            }
        }
}
