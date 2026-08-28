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
/// DataSyncContainersStore 保留旧同步步骤的顺序，仅负责本切片的持久化与读取。
/// </summary>
internal sealed class DataSyncContainersStore : DataSyncSliceBase
{
    public DataSyncContainersStore(DataSyncSliceContext context)
        : base(context)
    {
    }

        public async Task<SyncResult> SyncContainersFromHqAsync()
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

            try
            {
                Logger.LogInformation("开始从HQ数据库全量同步货柜数据...");

                // 1. 检查HQ数据库连接
                HqContext.CheckConnection();

                // 本地替换必须是一个事务：任何 HQ、映射或写入故障都不能留下已清空的旧数据。
                await LocalContext.Db.Ado.BeginTranAsync();
                try
                {
                    Logger.LogInformation("清空本地货柜数据...");
                    await LocalContext.Db.Deleteable<ContainerDetail>().ExecuteCommandAsync();
                    await LocalContext.Db.Deleteable<Container>().ExecuteCommandAsync();

                    const int batchSize = 5000;
                    var totalContainers = 0;
                    var totalDetails = 0;
                    var pageNumber = 1;

                    Logger.LogInformation("开始同步货柜主表数据...");
                    while (true)
                    {
                        var hqContainersBatch = await HqContext.CPT_RED_货柜单主表Db.AsQueryable()
                            .Skip((pageNumber - 1) * batchSize)
                            .Take(batchSize)
                            .ToListAsync();
                        if (!hqContainersBatch.Any())
                            break;

                        var localContainers = Mapper.Map<List<Container>>(hqContainersBatch);
                        if (localContainers.Any())
                        {
                            await LocalContext.Db.Insertable(localContainers).ExecuteCommandAsync();
                            totalContainers += localContainers.Count;
                        }

                        pageNumber++;
                    }

                    Logger.LogInformation("开始同步货柜明细表数据...");
                    pageNumber = 1;
                    while (true)
                    {
                        var hqDetailsBatch = await HqContext.CPT_RED_货柜单详情表Db.AsQueryable()
                            .Skip((pageNumber - 1) * batchSize * 10)
                            .Take(batchSize * 10)
                            .ToListAsync();
                        if (!hqDetailsBatch.Any())
                            break;

                        var localDetails = Mapper.Map<List<ContainerDetail>>(hqDetailsBatch);
                        if (localDetails.Any())
                        {
                            await LocalContext.Db.Insertable(localDetails).ExecuteCommandAsync();
                            totalDetails += localDetails.Count;
                        }

                        pageNumber++;
                    }

                    await LocalContext.Db.Ado.CommitTranAsync();
                    result.AddedCount = totalContainers + totalDetails;
                    result.IsSuccess = true;
                    result.Message = $"货柜数据全量同步成功，主表: {totalContainers} 条，明细: {totalDetails} 条";
                    Logger.LogInformation(result.Message);
                }
                catch
                {
                    await LocalContext.Db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步货柜数据时发生错误");
                result.Message = $"同步失败: {ex.Message}";
                result.ErrorCount = Math.Max(result.ErrorCount, 1);
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        public async Task<SyncResult> SyncContainersIncrementalFromHqAsync(DateTime lastUpdateDate)
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

            try
            {
                Logger.LogInformation(
                    $"开始从HQ数据库增量同步货柜数据，上次更新时间: {lastUpdateDate:yyyy-MM-dd HH:mm:ss}"
                );

                // 1. 检查HQ数据库连接
                HqContext.CheckConnection();

                // 2. 增量同步货柜主表数据
                const int batchSize = 1000;
                var totalContainers = 0;
                var totalDetails = 0;
                var pageNumber = 1;

                Logger.LogInformation("开始增量同步货柜主表数据...");

                // 转换日期格式用于HQ数据库查询
                var lastUpdateDateStr = lastUpdateDate.ToString("yyyy-MM-dd HH:mm:ss");

                while (true)
                {
                    // 查询HQ数据库中更新时间大于指定日期的货柜主表数据
                    var hqContainersBatch = await HqContext
                        .CPT_RED_货柜单主表Db.AsQueryable()
                        .Where(c =>
                            c.FGC_LastModifyDate != null && c.FGC_LastModifyDate > lastUpdateDate
                        )
                        .Skip((pageNumber - 1) * batchSize)
                        .Take(batchSize)
                        .ToListAsync();

                    if (!hqContainersBatch.Any())
                        break; // 没有更多数据

                    Logger.LogInformation(
                        $"从HQ数据库获取到第 {pageNumber} 批增量货柜主表数据，共 {hqContainersBatch.Count} 条"
                    );

                    // 获取现有的本地货柜数据
                    var hqGuids = hqContainersBatch
                        .Select(c => c.HGUID)
                        .Where(g => !string.IsNullOrEmpty(g))
                        .ToList();
                    var existingContainers = await LocalContext
                        .Db.Queryable<Container>()
                        .Where(c => hqGuids.Contains(c.ContainerCode))
                        .ToListAsync();

                    var containersToUpdate = new List<Container>();
                    var containersToAdd = new List<Container>();

                    foreach (var hqContainer in hqContainersBatch)
                    {
                        try
                        {
                            var containerCode = hqContainer.HGUID ?? UuidHelper.GenerateUuid7();
                            var existingContainer = existingContainers.FirstOrDefault(c =>
                                c.ContainerCode == containerCode
                            );

                            // 使用AutoMapper转换HQ数据到本地实体
                            var localContainer = Mapper.Map<Container>(hqContainer);

                            if (existingContainer != null)
                            {
                                // 更新现有记录，保留原创建信息
                                localContainer.CreatedAt = existingContainer.CreatedAt;
                                localContainer.CreatedBy = existingContainer.CreatedBy;
                                containersToUpdate.Add(localContainer);
                            }
                            else
                            {
                                // 新增记录，AutoMapper已经处理了创建信息
                                containersToAdd.Add(localContainer);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(
                                $"处理增量货柜主表数据失败，跳过记录 {hqContainer.HGUID}: {ex.Message}"
                            );
                            result.ErrorCount++;
                        }
                    }

                    // 批量更新和新增
                    if (containersToUpdate.Any())
                    {
                        await LocalContext.Db.Updateable(containersToUpdate).ExecuteCommandAsync();
                        result.UpdatedCount += containersToUpdate.Count;
                        totalContainers += containersToUpdate.Count;
                    }

                    if (containersToAdd.Any())
                    {
                        await LocalContext.Db.Insertable(containersToAdd).ExecuteCommandAsync();
                        result.AddedCount += containersToAdd.Count;
                        totalContainers += containersToAdd.Count;
                    }

                    pageNumber++;
                }

                Logger.LogInformation($"货柜主表增量同步完成，共处理 {totalContainers} 条记录");

                // 3. 增量同步货柜明细表数据
                Logger.LogInformation("开始增量同步货柜明细表数据...");
                pageNumber = 1;

                while (true)
                {
                    // 查询HQ数据库中更新时间大于指定日期的货柜明细数据
                    var hqDetailsBatch = await HqContext
                        .CPT_RED_货柜单详情表Db.AsQueryable()
                        .Where(d =>
                            d.FGC_LastModifyDate != null && d.FGC_LastModifyDate > lastUpdateDate
                        )
                        .Skip((pageNumber - 1) * batchSize)
                        .Take(batchSize)
                        .ToListAsync();

                    if (!hqDetailsBatch.Any())
                        break; // 没有更多数据

                    Logger.LogInformation(
                        $"从HQ数据库获取到第 {pageNumber} 批增量货柜明细数据，共 {hqDetailsBatch.Count} 条"
                    );

                    // 获取现有的本地货柜明细数据
                    var hqDetailGuids = hqDetailsBatch
                        .Select(d => d.HGUID)
                        .Where(g => !string.IsNullOrEmpty(g))
                        .ToList();
                    var existingDetails = await LocalContext
                        .Db.Queryable<ContainerDetail>()
                        .Where(d => hqDetailGuids.Contains(d.DetailCode))
                        .ToListAsync();

                    var detailsToUpdate = new List<ContainerDetail>();
                    var detailsToAdd = new List<ContainerDetail>();

                    foreach (var hqDetail in hqDetailsBatch)
                    {
                        try
                        {
                            var detailCode = hqDetail.HGUID ?? UuidHelper.GenerateUuid7();
                            var existingDetail = existingDetails.FirstOrDefault(d =>
                                d.DetailCode == detailCode
                            );

                            // 使用AutoMapper转换HQ数据到本地实体
                            var localDetail = Mapper.Map<ContainerDetail>(hqDetail);

                            if (existingDetail != null)
                            {
                                // 更新现有记录，保留原创建信息
                                localDetail.CreatedAt = existingDetail.CreatedAt;
                                localDetail.CreatedBy = existingDetail.CreatedBy;
                                detailsToUpdate.Add(localDetail);
                            }
                            else
                            {
                                // 新增记录，AutoMapper已经处理了创建信息
                                detailsToAdd.Add(localDetail);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(
                                $"处理增量货柜明细数据失败，跳过记录 {hqDetail.HGUID}: {ex.Message}"
                            );
                            result.ErrorCount++;
                        }
                    }

                    // 批量更新和新增明细数据
                    if (detailsToUpdate.Any())
                    {
                        await LocalContext.Db.Updateable(detailsToUpdate).ExecuteCommandAsync();
                        totalDetails += detailsToUpdate.Count;
                    }

                    if (detailsToAdd.Any())
                    {
                        await LocalContext.Db.Insertable(detailsToAdd).ExecuteCommandAsync();
                        totalDetails += detailsToAdd.Count;
                    }

                    pageNumber++;
                }

                Logger.LogInformation($"货柜明细增量同步完成，共处理 {totalDetails} 条记录");

                // 4. 设置同步结果
                result.IsSuccess = true;
                result.Message =
                    $"货柜数据增量同步成功，主表: {totalContainers} 条，明细: {totalDetails} 条";
                Logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "增量同步货柜数据时发生错误");
                result.Message = $"增量同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }
}
