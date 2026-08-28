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
/// DataSyncStorePricesStore 保留旧同步步骤的顺序，仅负责本切片的持久化与读取。
/// </summary>
internal sealed class DataSyncStorePricesStore : DataSyncSliceBase
{
    public DataSyncStorePricesStore(DataSyncSliceContext context)
        : base(context)
    {
    }

        public async Task<SyncResult> SyncStoreRetailPricesFromHqAsync(
            List<string>? selectedStoreCodes = null
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };

            try
            {
                Logger.LogInformation(
                    $"🔄 开始从HQ数据库同步分店零售价数据{(selectedStoreCodes?.Any() == true ? $"，指定分店: {string.Join(", ", selectedStoreCodes)}" : "，全部分店")}"
                );

                // 🚀 使用JOIN查询获取有效的HQ数据（商品信息存在且分店代码匹配）
                var query = HqContext
                    .Db.Queryable<DIC_商品零售价表, DIC_商品信息字典表>(
                        (price, product) =>
                            new JoinQueryInfos(JoinType.Inner, price.H商品编码 == product.H商品编码)
                    )
                    .Where(
                        (price, product) =>
                            !string.IsNullOrEmpty(price.H商品编码)
                            && !string.IsNullOrEmpty(price.H分店代码)
                            && price.H使用状态 == true
                            && product.H使用状态 == true
                    );

                // 如果指定了分店代码，添加分店代码过滤条件
                if (selectedStoreCodes?.Any() == true)
                {
                    query = query.Where(
                        (price, product) => selectedStoreCodes.Contains(price.H分店代码)
                    );
                }

                var hqRetailPrices = await query.Select((price, product) => price).ToListAsync();

                Logger.LogInformation(
                    $"📊 从HQ获取到 {hqRetailPrices.Count:N0} 条有效的分店零售价记录（已过滤无效商品和分店）"
                );

                if (!hqRetailPrices.Any())
                {
                    result.Message = "✅ HQ数据库中没有分店零售价数据，同步完成";
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
                            $"🗑️ 正在清空指定分店的零售价数据: {string.Join(", ", selectedStoreCodes)}"
                        );
                        await db.Deleteable<StoreRetailPrice>()
                            .Where(x =>
                                x.StoreCode != null && selectedStoreCodes.Contains(x.StoreCode)
                            )
                            .ExecuteCommandAsync();
                        Logger.LogInformation("✅ 指定分店的零售价数据已清空");
                    }
                    else
                    {
                        Logger.LogInformation("🗑️ 正在清空本地分店零售价表...");
                        await db.Deleteable<StoreRetailPrice>().ExecuteCommandAsync();
                        Logger.LogInformation("✅ 本地分店零售价表已清空");
                    }

                    int totalProcessed = 0;
                    int totalAdded = 0;
                    const int batchSize = 50000;

                    // 转换数据 - 使用AutoMapper
                    Logger.LogInformation("🔄 开始转换数据格式 (使用AutoMapper)...");
                    var localRetailPrices = Mapper.Map<List<StoreRetailPrice>>(hqRetailPrices);
                    Logger.LogInformation(
                        $"✅ 数据转换完成，共 {localRetailPrices.Count:N0} 条记录"
                    );

                    // 使用PageSize优化的批量插入
                    Logger.LogInformation(
                        $"📦 开始批量插入 {localRetailPrices.Count:N0} 条记录..."
                    );
                    // BulkCopy 异常必须交给外层唯一事务边界回滚，不能吞掉后再提交删除结果。
                    await db.Fastest<StoreRetailPrice>()
                        .PageSize(batchSize) // 让SqlSugar内部处理分批
                        .BulkCopyAsync(localRetailPrices);

                    totalAdded = localRetailPrices.Count;
                    totalProcessed = localRetailPrices.Count;

                    Logger.LogInformation($"✅ 批量插入完成，成功插入 {totalAdded:N0} 条记录");

                    // 提交事务
                    await db.Ado.CommitTranAsync();
                    Logger.LogInformation("✅ 事务提交成功");

                    result.AddedCount = totalAdded;
                    result.ErrorCount = 0;
                    result.IsSuccess = true;
                    result.Message = $"🎉 分店零售价数据同步成功！共处理 {totalProcessed:N0} 条记录，全部成功插入";

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
                Logger.LogError(ex, "❌ 同步分店零售价数据时发生错误");
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
}
