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

namespace BlazorApp.Api.Features.DataSync.Locations;

/// <summary>
/// DataSyncLocationsStore 保留旧同步步骤的顺序，仅负责本切片的持久化与读取。
/// </summary>
internal sealed class DataSyncLocationsStore : DataSyncSliceBase
{
    public DataSyncLocationsStore(DataSyncSliceContext context)
        : base(context)
    {
    }

        public async Task<SyncResult> SyncLocationsFromHqAsync()
        {
            var startTime = DateTime.Now;
            try
            {
                Logger.LogInformation("开始从HQ数据库同步货位信息数据...");

                // 读取、映射和磁盘暂存逐批进行，不能在内存中同时保留完整 HQ 与 Location 两套对象图。
                const int batchSize = 5000;
                var processedCount = 0;
                var sourceReader = new DataSyncLocationsSourceReader(HqContext);
                var mapper = new DataSyncLocationsEntityMapper(Mapper);
                await using var spool = new DataSyncLocationsSpool(Logger);

                await foreach (var sourceBatch in sourceReader.ReadBatchesAsync(batchSize))
                {
                    var locations = mapper.MapBatch(sourceBatch, DateTime.Now);
                    await spool.WriteBatchAsync(locations, CancellationToken.None);
                    processedCount += sourceBatch.Count;
                }

                // 临时文件完成并刷盘后才允许开启本地事务；远端读取和映射绝不占用线上表事务。
                await spool.CompleteWritingAsync(CancellationToken.None);
                await new DataSyncLocationsTransactionWriter(LocalContext).WriteAsync(spool);

                var result = DataSyncLocationsResultAssembler.CreateSuccess(
                    startTime,
                    processedCount
                );
                Logger.LogInformation(result.Message);
                return result;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步货位信息数据时发生错误");
                return DataSyncLocationsResultAssembler.CreateFailure(startTime, ex);
            }
        }

        public async Task<SyncResult> SyncProductLocationsFromHqAsync()
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
                    "🚀 开始从HQ数据库同步货位商品关联数据（AutoMapper模式）..."
                );

                // 1. 检查HQ数据库连接
                HqContext.CheckConnection();

                // 2. 从HQ数据库获取货位存货信息和配货信息数据 (使用批量操作)
                const int batchSize = 5000; // 每批处理5000条记录
                var totalProcessed = 0;
                var totalAdded = 0;
                var totalUpdated = 0;
                var totalErrors = 0;
                var pageNumber = 1;

                while (true)
                {
                    // 获取货位存货信息
                    var hqStockLocationsBatch = await HqContext
                        .CPT_RED_货位存货信息表Db.AsQueryable()
                        .Skip((pageNumber - 1) * batchSize / 2)
                        .Take(batchSize / 2)
                        .ToListAsync();

                    // 获取货位配货信息
                    var hqPickLocationsBatch = await HqContext
                        .CPT_RED_货位配货信息表Db.AsQueryable()
                        .Skip((pageNumber - 1) * batchSize / 2)
                        .Take(batchSize / 2)
                        .ToListAsync();

                    if (!hqStockLocationsBatch.Any() && !hqPickLocationsBatch.Any())
                        break; // 没有更多数据

                    Logger.LogInformation(
                        $"从HQ数据库获取到第 {pageNumber} 批货位商品关联信息 - 存货: {hqStockLocationsBatch.Count}条, 配货: {hqPickLocationsBatch.Count}条"
                    );

                    // 🚀 收集所有需要查询的货位编码（从两个实体中）
                    var locationCodes = new HashSet<string>();

                    // 收集货位存货信息中的货位编码
                    foreach (
                        var hqStockLocation in hqStockLocationsBatch.Where(x => x?.货位编码 != null)
                    )
                    {
                        locationCodes.Add(hqStockLocation.货位编码!);
                    }

                    // 收集货位配货信息中的货位编码
                    foreach (
                        var hqPickLocation in hqPickLocationsBatch.Where(x => x?.货位编码 != null)
                    )
                    {
                        locationCodes.Add(hqPickLocation.货位编码!);
                    }

                    // 🚀 批量查询所有需要的货位信息，建立编码到GUID的映射
                    var locationDict = new Dictionary<string, string>();
                    if (locationCodes.Any())
                    {
                        var locations = await LocalContext
                            .LocationDb.AsQueryable()
                            .Where(l =>
                                l.LocationCode != null && locationCodes.Contains(l.LocationCode)
                            )
                            .ToListAsync();

                        // 创建货位编码到货位GUID的字典，方便快速查找
                        locationDict = locations
                            .Where(l =>
                                !string.IsNullOrEmpty(l.LocationCode)
                                && !string.IsNullOrEmpty(l.LocationGuid)
                            )
                            .ToDictionary(l => l.LocationCode!, l => l.LocationGuid!);
                    }

                    // 🚀 使用AutoMapper将HQ货位存货信息转换为本地ProductLocation实体
                    var stockProductLocations = Mapper.Map<List<ProductLocation>>(
                        hqStockLocationsBatch
                    );

                    // 🚀 使用AutoMapper将HQ货位配货信息转换为本地ProductLocation实体
                    var pickProductLocations = Mapper.Map<List<ProductLocation>>(
                        hqPickLocationsBatch
                    );

                    // 🚀 合并两个列表，并更新LocationGuid（从货位编码转换为GUID）
                    var allProductLocations = new List<ProductLocation>();

                    // 处理存货信息转换的ProductLocation
                    foreach (
                        var productLocation in stockProductLocations.Where(pl =>
                            !string.IsNullOrEmpty(pl.ProductCode)
                        )
                    )
                    {
                        // 根据货位编码从字典中查找真实的LocationGuid
                        if (
                            locationDict.TryGetValue(
                                productLocation.LocationGuid ?? "",
                                out var realLocationGuid
                            )
                        )
                        {
                            productLocation.LocationGuid = realLocationGuid;
                            allProductLocations.Add(productLocation);
                        }
                        else
                        {
                            Logger.LogWarning(
                                $"⚠️ 存货记录中找不到货位编码 {productLocation.LocationGuid} 对应的Location"
                            );
                        }
                    }

                    // 处理配货信息转换的ProductLocation
                    foreach (
                        var productLocation in pickProductLocations.Where(pl =>
                            !string.IsNullOrEmpty(pl.ProductCode)
                        )
                    )
                    {
                        // 根据货位编码从字典中查找真实的LocationGuid
                        if (
                            locationDict.TryGetValue(
                                productLocation.LocationGuid ?? "",
                                out var realLocationGuid
                            )
                        )
                        {
                            productLocation.LocationGuid = realLocationGuid;
                            allProductLocations.Add(productLocation);
                        }
                        else
                        {
                            Logger.LogWarning(
                                $"⚠️ 配货记录中找不到货位编码 {productLocation.LocationGuid} 对应的Location"
                            );
                        }
                    }

                    Logger.LogInformation(
                        $"AutoMapper转换完成，生成 {allProductLocations.Count} 个ProductLocation对象（存货: {stockProductLocations.Count}, 配货: {pickProductLocations.Count}）"
                    );

                    // 🚀 使用Storageable处理Insert/Update逻辑
                    try
                    {
                        if (allProductLocations.Any())
                        {
                            var storageResult = await LocalContext
                                .Db.Storageable(allProductLocations)
                                .WhereColumns(x => x.Guid) // 基于GUID进行判断
                                .ToStorageAsync();

                            // 执行插入和更新
                            var insertResult = storageResult.AsInsertable.ExecuteCommand();
                            var updateResult = storageResult.AsUpdateable.ExecuteCommand();

                            totalAdded += insertResult;
                            totalUpdated += updateResult;

                            Logger.LogInformation(
                                $"第 {pageNumber} 批货位关联AutoMapper处理完成 - 新增: {insertResult}, 更新: {updateResult}"
                            );
                        }
                        else
                        {
                            Logger.LogInformation(
                                $"第 {pageNumber} 批货位关联AutoMapper转换后无有效数据"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"第 {pageNumber} 批货位商品关联AutoMapper处理失败");
                        totalErrors += hqStockLocationsBatch.Count + hqPickLocationsBatch.Count;
                    }

                    totalProcessed += hqStockLocationsBatch.Count + hqPickLocationsBatch.Count;
                    pageNumber++;
                }

                result.AddedCount = totalAdded;
                result.UpdatedCount = totalUpdated;
                result.ErrorCount = totalErrors;
                result.IsSuccess = totalErrors == 0;
                result.Message =
                    $"货位商品关联信息同步完成（使用AutoMapper转换）！总共处理: {totalProcessed}, 新增: {totalAdded}, 更新: {totalUpdated}, 错误: {totalErrors}";
                Logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步货位商品关联数据时发生错误");
                result.Message = $"同步失败: {ex.Message}";
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
